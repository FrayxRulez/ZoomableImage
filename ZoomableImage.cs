using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows;
using System.Windows.Media;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Diagnostics;

namespace Microsoft.Phone.Controls
{
    public sealed class ZoomableImage : Control
    {
        private ViewportControl Viewport;

        private Canvas Canvas;

        private Image Presenter;

        private ScaleTransform Xform;

        public ZoomableImage()
        {
            DefaultStyleKey = typeof(ZoomableImage);
        }

        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();

            Viewport = (ViewportControl)GetTemplateChild("Viewport");
            Canvas = (Canvas)GetTemplateChild("Canvas");
            Presenter = (Image)GetTemplateChild("Presenter");
            Xform = (ScaleTransform)GetTemplateChild("Xform");

            Viewport.ManipulationStarted += OnManipulationStarted;
            Viewport.ManipulationDelta += OnManipulationDelta;
            Viewport.ManipulationCompleted += OnManipulationCompleted;
            Viewport.DoubleTap += OnDoubleTap;
            Viewport.ViewportChanged += OnViewportChanged;

            Presenter.ImageOpened += OnImageOpened;
        }

        #region Source
        public ImageSource Source
        {
            get { return (ImageSource)GetValue(SourceProperty); }
            set { SetValue(SourceProperty, value); }
        }

        public static readonly DependencyProperty SourceProperty =
            DependencyProperty.Register("Source", typeof(ImageSource), typeof(ZoomableImage), new PropertyMetadata(null));
        #endregion

        #region Stretch
        public Stretch Stretch
        {
            get { return (Stretch)GetValue(StretchProperty); }
            set { SetValue(StretchProperty, value); }
        }

        public static readonly DependencyProperty StretchProperty =
            DependencyProperty.Register("Stretch", typeof(Stretch), typeof(ZoomableImage), new PropertyMetadata(Stretch.Fill));
        #endregion

        #region MinZoomMode
        public ZoomMode MinZoomMode
        {
            get { return (ZoomMode)GetValue(MinZoomModeProperty); }
            set { SetValue(MinZoomModeProperty, value); }
        }

        public static readonly DependencyProperty MinZoomModeProperty =
            DependencyProperty.Register("MinZoomMode", typeof(ZoomMode), typeof(ZoomableImage), new PropertyMetadata(ZoomMode.FitWidth));
        #endregion

        const double MaxScale = 10;

        double _scale = 1.0;
        double _minScale;
        double _coercedScale;
        double _originalScale;
        double _baseScale;

        Size _viewportSize;
        bool _pinching;
        Point _screenMidpoint;
        Point _relativeMidpoint;

        BitmapImage _bitmap;
        Size _textureScale;

        private void OnManipulationStarted(object sender, ManipulationStartedEventArgs e)
        {
            _pinching = false;
            _originalScale = _scale;
        }

        private void OnManipulationDelta(object sender, ManipulationDeltaEventArgs e)
        {
            if (e.PinchManipulation != null)
            {
                e.Handled = true;

                if (!_pinching)
                {
                    _pinching = true;
                    Point center = e.PinchManipulation.Original.Center;
                    _relativeMidpoint = new Point(center.X / Presenter.ActualWidth, center.Y / Presenter.ActualHeight);
                    var xform = Presenter.TransformToVisual(Viewport);
                    _screenMidpoint = xform.Transform(center);
                }

                _scale = _originalScale * e.PinchManipulation.CumulativeScale;

                CoerceScale(false);
                ResizeImage(false);
            }
            else if (_pinching)
            {
                _pinching = false;
                _originalScale = _scale = _coercedScale;
            }
        }

        private void OnManipulationCompleted(object sender, ManipulationCompletedEventArgs e)
        {
            _pinching = false;
            _scale = _coercedScale;
        }

        private void OnViewportChanged(object sender, ViewportChangedEventArgs e)
        {
            Size newSize = new Size(Viewport.Viewport.Width, Viewport.Viewport.Height);
            if (newSize != _viewportSize)
            {
                _viewportSize = newSize;
                CoerceScale(true);
                ResizeImage(false);
            }
        }

        private void ResizeImage(bool center)
        {
            if (_coercedScale != 0 && _bitmap != null)
            {
                var newWidth = Canvas.Width = Math.Round(_textureScale.Width * _coercedScale);
                var newHeight = Canvas.Height = Math.Round(_textureScale.Height * _coercedScale);

                Xform.ScaleX = Xform.ScaleY = _coercedScale;
                Viewport.Bounds = new Rect(0, 0, newWidth, newHeight);

                if (center)
                {
                    Viewport.SetViewportOrigin(new Point(
                        Math.Round((newWidth - Viewport.ActualWidth) / 2),
                        Math.Round((newHeight - Viewport.ActualHeight) / 2)
                        ));
                }
                else
                {
                    var newImgMid = new Point(newWidth * _relativeMidpoint.X, newHeight * _relativeMidpoint.Y);
                    var origin = new Point(newImgMid.X - _screenMidpoint.X, newImgMid.Y - _screenMidpoint.Y);
                    Viewport.SetViewportOrigin(origin);
                }
            }
        }

        private void CoerceScale(bool recompute)
        {
            if (recompute && _bitmap != null && Viewport != null)
            {
                var minX = Viewport.ActualWidth / _textureScale.Width;
                var minY = Viewport.ActualHeight / _textureScale.Height;

                if (MinZoomMode == ZoomMode.Coerence)
                {
                    _minScale = Math.Min(minX, minY);
                }
                else
                {
                    _minScale = MinZoomMode == ZoomMode.FitWidth ? minX : minY;
                }
            }

            _coercedScale = Math.Min(MaxScale, Math.Max(_scale, _minScale));
        }

        private void OnImageOpened(object sender, RoutedEventArgs e)
        {
            var temp = (BitmapImage)Presenter.Source;
            if (temp.PixelHeight > 2048 || temp.PixelWidth > 2048)
            {

                _textureScale = ScaleImage(temp.PixelWidth, temp.PixelHeight, 2048, 2048);
                Presenter.Width = _textureScale.Width;
                Presenter.Height = _textureScale.Height;
            }
            else
            {
                _textureScale = new Size(temp.PixelWidth, temp.PixelHeight);
            }

            _bitmap = temp;
            _scale = 0;
            CoerceScale(true);
            _baseScale = _scale = _coercedScale;

            ResizeImage(true);

            if (MinZoomMode != ZoomMode.Coerence)
            {
                Viewport.SetViewportOrigin(new Point(0, 0));
            }
        }

        private Size ScaleImage(int actualWidth, int actualHeight, int maxWidth, int maxHeight)
        {
            var ratioX = (double)maxWidth / actualWidth;
            var ratioY = (double)maxHeight / actualHeight;
            var ratio = Math.Min(ratioX, ratioY);

            var newWidth = (int)(actualWidth * ratio);
            var newHeight = (int)(actualHeight * ratio);

            return new Size(newWidth, newHeight);
        }

        private void OnDoubleTap(object sender, GestureEventArgs e)
        {
            Point center = e.GetPosition(Presenter);
            _relativeMidpoint = new Point(center.X / Presenter.ActualWidth, center.Y / Presenter.ActualHeight);
            var xform = Presenter.TransformToVisual(Viewport);
            _screenMidpoint = xform.Transform(center);

            if (_scale > _baseScale)
            {
                _scale = 0;
                CoerceScale(true);
                _scale = _coercedScale;
                ResizeImage(true);
            }
            else
            {
                _scale = _baseScale * 3;
                CoerceScale(false);
                ResizeImage(false);
            }
        }
    }

    public enum ZoomMode
    {
        Coerence,
        FitWidth,
        FitHeight
    }
}
