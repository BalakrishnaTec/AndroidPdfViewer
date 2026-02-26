using Android.Content;
using Android.Graphics;
using Android.Util;
using Android.Views;
using Android.Widget;
using AndroidX.AppCompat.Widget;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace AndroidPdfViewer;

[Android.Runtime.Register("androidpdfviewer.ZoomablePdfPageView")]
public class ZoomablePdfPageView : FrameLayout
{
    private readonly AppCompatImageView _imageView;
    private readonly ScaleGestureDetector _scaleGestureDetector;
    private readonly GestureDetector _gestureDetector;
    private CancellationTokenSource? _renderCancellationTokenSource;
    private CancellationTokenSource? _gestureRenderCts;

    private PdfDocument? _document;
    private int _pageIndex;
    private float _zoomFactor = 1f;
    private bool _isScaling;
    private Matrix? _imageMatrix;
    private int _currentBitmapWidth;
    private int _currentBitmapHeight;
    private float _lastRenderedZoom = 1f;
    private float _gestureFocusX;
    private float _gestureFocusY;
    private float _panX;
    private float _panY;
    private int _activePointerId = -1;
    private float _lastTouchX;
    private float _lastTouchY;
    private float _lastNotifiedZoom = -1f;

    public ZoomablePdfPageView(Context context) : this(context, null)
    {
    }

    public ZoomablePdfPageView(Context context, IAttributeSet? attrs) : this(context, attrs, 0)
    {
    }

    public ZoomablePdfPageView(Context context, IAttributeSet? attrs, int defStyleAttr) : base(context, attrs, defStyleAttr)
    {
        _imageView = new AppCompatImageView(context)
        {
            LayoutParameters = new LayoutParams(LayoutParams.MatchParent, LayoutParams.MatchParent)
        };

        _imageView.SetScaleType(AppCompatImageView.ScaleType.Matrix);
        _imageView.SetBackgroundColor(Color.White);

        AddView(_imageView);

        _scaleGestureDetector = new ScaleGestureDetector(context, new ZoomScaleListener(this));
        _gestureDetector = new GestureDetector(context, new TapGestureListener(this));
        Clickable = true;
    }

    public float MinZoom { get; set; } = 1f;

    public float MaxZoom { get; set; } = 6f;

    public float DoubleTapZoom { get; set; } = 4f;

    public float ZoomFactor => _zoomFactor;

    public event Action<float>? ZoomFactorChanged;
    public event Action<float, float, float>? ViewStateChanged;

    public float PanX => _panX;

    public float PanY => _panY;

    public void SetViewState(float zoomFactor, float panX = 0f, float panY = 0f)
    {
        _zoomFactor = Math.Clamp(zoomFactor, MinZoom, MaxZoom);
        _panX = panX;
        _panY = panY;
    }

    public async Task LoadPageAsync(PdfDocument document, int pageIndex = 0, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);

        _document = document;
        _pageIndex = pageIndex;
        _zoomFactor = Math.Clamp(_zoomFactor, MinZoom, MaxZoom);

        await RenderAsync(cancellationToken);
    }

    public Task SetZoomAsync(float zoomFactor, CancellationToken cancellationToken = default)
    {
        _zoomFactor = Math.Clamp(zoomFactor, MinZoom, MaxZoom);
        return RenderAsync(cancellationToken);
    }

    public Task ZoomInAsync(float step = 0.25f, CancellationToken cancellationToken = default)
    {
        return SetZoomAsync(_zoomFactor + Math.Abs(step), cancellationToken);
    }

    public Task ZoomOutAsync(float step = 0.25f, CancellationToken cancellationToken = default)
    {
        return SetZoomAsync(_zoomFactor - Math.Abs(step), cancellationToken);
    }

    public override bool OnInterceptTouchEvent(MotionEvent? ev)
    {
        if (_isScaling || (ev?.PointerCount ?? 0) > 1)
        {
            Parent?.RequestDisallowInterceptTouchEvent(true);
            return true;
        }

        if (ev is not null && ev.PointerCount == 1 && CanPanCurrentView())
        {
            if (ev.ActionMasked is MotionEventActions.Down or MotionEventActions.Move)
            {
                Parent?.RequestDisallowInterceptTouchEvent(true);
                return true;
            }
        }

        return base.OnInterceptTouchEvent(ev);
    }

    public override bool DispatchTouchEvent(MotionEvent? e)
    {
        if (e is not null)
        {
            _gestureDetector.OnTouchEvent(e);
            _scaleGestureDetector.OnTouchEvent(e);

            if (_isScaling || e.PointerCount > 1)
            {
                Parent?.RequestDisallowInterceptTouchEvent(true);
            }

            if (e.ActionMasked is MotionEventActions.Up or MotionEventActions.Cancel)
            {
                Parent?.RequestDisallowInterceptTouchEvent(false);
            }
        }

        return base.DispatchTouchEvent(e);
    }

    private void ToggleZoomAt(float focusX, float focusY)
    {
        if (Width <= 0 || Height <= 0)
        {
            return;
        }

        _gestureFocusX = float.IsNaN(focusX) || float.IsInfinity(focusX)
            ? Width / 2f
            : Math.Clamp(focusX, 0f, Math.Max(0f, Width));
        _gestureFocusY = float.IsNaN(focusY) || float.IsInfinity(focusY)
            ? Height / 2f
            : Math.Clamp(focusY, 0f, Math.Max(0f, Height));

        var zoomThreshold = MinZoom + 0.01f;
        if (_zoomFactor > zoomThreshold)
        {
            _zoomFactor = MinZoom;
            _panX = 0f;
            _panY = 0f;
        }
        else
        {
            var targetZoom = Math.Max(MinZoom + 0.1f, DoubleTapZoom);
            _zoomFactor = Math.Clamp(targetZoom, MinZoom, MaxZoom);
        }

        _isScaling = false;
        NotifyZoomFactorChanged();
        ApplyGesturePreviewMatrix();
        RequestRenderFromGesture();
    }

    public override bool OnTouchEvent(MotionEvent? e)
    {
        if (e is null)
        {
            return false;
        }

        _scaleGestureDetector.OnTouchEvent(e);

        switch (e.ActionMasked)
        {
            case MotionEventActions.Down:
                _activePointerId = e.GetPointerId(0);
                _lastTouchX = e.GetX(0);
                _lastTouchY = e.GetY(0);
                if (CanPanCurrentView())
                {
                    Parent?.RequestDisallowInterceptTouchEvent(true);
                    return true;
                }
                break;
            case MotionEventActions.Move:
                if (!_isScaling && e.PointerCount == 1 && _activePointerId != -1 && CanPanCurrentView())
                {
                    var pointerIndex = e.FindPointerIndex(_activePointerId);
                    if (pointerIndex >= 0)
                    {
                        var touchX = e.GetX(pointerIndex);
                        var touchY = e.GetY(pointerIndex);
                        var deltaX = touchX - _lastTouchX;
                        var deltaY = touchY - _lastTouchY;

                        _lastTouchX = touchX;
                        _lastTouchY = touchY;

                        _panX += deltaX;
                        _panY += deltaY;
                        ApplyGesturePreviewMatrix();
                        Parent?.RequestDisallowInterceptTouchEvent(true);
                        return true;
                    }
                }
                break;
            case MotionEventActions.PointerUp:
                if (_activePointerId == e.GetPointerId(e.ActionIndex))
                {
                    _activePointerId = -1;
                }
                break;
            case MotionEventActions.Up:
            case MotionEventActions.Cancel:
                _activePointerId = -1;
                if (!_isScaling)
                {
                    Parent?.RequestDisallowInterceptTouchEvent(false);
                }
                break;
        }

        if (_isScaling || e.PointerCount > 1)
        {
            Parent?.RequestDisallowInterceptTouchEvent(true);
            return true;
        }

        if (CanPanCurrentView())
        {
            return true;
        }

        return base.OnTouchEvent(e);
    }

    private bool CanPanCurrentView()
    {
        if (_currentBitmapWidth <= 0 || _currentBitmapHeight <= 0 || Width <= 0 || Height <= 0)
        {
            return false;
        }

        var relativeScale = _lastRenderedZoom > 0f
            ? Math.Clamp(_zoomFactor / _lastRenderedZoom, 0.25f, 8f)
            : 1f;

        var scaledWidth = _currentBitmapWidth * relativeScale;
        var scaledHeight = _currentBitmapHeight * relativeScale;

        return scaledWidth > Width + 0.5f || scaledHeight > Height + 0.5f;
    }

    private void UpdateZoomFromGesture(float gestureScaleFactor, float focusX, float focusY)
    {
        if (gestureScaleFactor <= 0f || float.IsNaN(gestureScaleFactor) || float.IsInfinity(gestureScaleFactor))
        {
            return;
        }

        _zoomFactor = Math.Clamp(_zoomFactor * gestureScaleFactor, MinZoom, MaxZoom);
        _gestureFocusX = float.IsNaN(focusX) || float.IsInfinity(focusX)
            ? Width / 2f
            : Math.Clamp(focusX, 0f, Math.Max(0f, Width));
        _gestureFocusY = float.IsNaN(focusY) || float.IsInfinity(focusY)
            ? Height / 2f
            : Math.Clamp(focusY, 0f, Math.Max(0f, Height));

        NotifyZoomFactorChanged();
        ApplyGesturePreviewMatrix();
        RequestRenderFromGesture();
    }

    private void NotifyZoomFactorChanged()
    {
        if (Math.Abs(_lastNotifiedZoom - _zoomFactor) < 0.01f)
        {
            return;
        }

        _lastNotifiedZoom = _zoomFactor;
        ZoomFactorChanged?.Invoke(_zoomFactor);
    }

    private void NotifyViewStateChanged()
    {
        ViewStateChanged?.Invoke(_zoomFactor, _panX, _panY);
    }

    private void ApplyGesturePreviewMatrix()
    {
        if (_currentBitmapWidth <= 0 || _currentBitmapHeight <= 0 || Width <= 0 || Height <= 0)
        {
            return;
        }

        var relativeScale = _lastRenderedZoom > 0f
            ? Math.Clamp(_zoomFactor / _lastRenderedZoom, 0.25f, 8f)
            : 1f;
        var maxPreviewRelativeScale = GetMaxPreviewRelativeScale();
        relativeScale = Math.Clamp(relativeScale, 0.25f, maxPreviewRelativeScale);

        _imageMatrix?.Dispose();
        _imageMatrix = new Matrix();

        var dx = (Width - _currentBitmapWidth) / 2f;
        var dy = (Height - _currentBitmapHeight) / 2f;
        var pivotX = _gestureFocusX > 0f ? _gestureFocusX : Width / 2f;
        var pivotY = _gestureFocusY > 0f ? _gestureFocusY : Height / 2f;

        var contentWidth = _currentBitmapWidth * relativeScale;
        var contentHeight = _currentBitmapHeight * relativeScale;

        var baseTranslateX = dx + (1f - relativeScale) * pivotX;
        var baseTranslateY = dy + (1f - relativeScale) * pivotY;

        if (contentWidth <= Width)
        {
            _panX = (Width - contentWidth) / 2f - baseTranslateX;
        }
        else
        {
            var minPanX = Width - contentWidth - baseTranslateX;
            var maxPanX = -baseTranslateX;
            _panX = Math.Clamp(_panX, minPanX, maxPanX);
        }

        if (contentHeight <= Height)
        {
            _panY = (Height - contentHeight) / 2f - baseTranslateY;
        }
        else
        {
            var minPanY = Height - contentHeight - baseTranslateY;
            var maxPanY = -baseTranslateY;
            _panY = Math.Clamp(_panY, minPanY, maxPanY);
        }

        _imageMatrix.PostTranslate(dx, dy);

        _imageMatrix.PostScale(relativeScale, relativeScale, pivotX, pivotY);
        _imageMatrix.PostTranslate(_panX, _panY);
        _imageView.ImageMatrix = _imageMatrix;
        NotifyViewStateChanged();
    }

    private float GetMaxPreviewRelativeScale()
    {
        var currentLongestSide = Math.Max(_currentBitmapWidth, _currentBitmapHeight);
        if (currentLongestSide <= 0)
        {
            return 8f;
        }

        var maxBitmapSide = Math.Max(512, _document?.MaxBitmapSide ?? 4096);
        var maxRelative = (float)maxBitmapSide / currentLongestSide;

        return Math.Clamp(maxRelative, 1f, 8f);
    }

    private void RequestRenderFromGesture()
    {
        _gestureRenderCts?.Cancel();
        _gestureRenderCts?.Dispose();
        _gestureRenderCts = new CancellationTokenSource();

        var token = _gestureRenderCts.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                // Small debounce to avoid excessive re-rendering while fingers move.
                await Task.Delay(32, token).ConfigureAwait(false);
                await RenderAsync(token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }, token);
    }

    protected override async void OnSizeChanged(int width, int height, int oldw, int oldh)
    {
        base.OnSizeChanged(width, height, oldw, oldh);

        if (_document is not null && width > 0 && height > 0)
        {
            await RenderAsync();
        }
    }

    private async Task WaitForValidSizeAsync(CancellationToken cancellationToken)
    {
        if (Width > 0 && Height > 0)
        {
            return;
        }

        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        EventHandler<View.LayoutChangeEventArgs>? handler = null;

        handler = (_, _) =>
        {
            if (Width > 0 && Height > 0)
            {
                LayoutChange -= handler;
                tcs.TrySetResult(true);
            }
        };

        LayoutChange += handler;

        using var ctr = cancellationToken.Register(() =>
        {
            LayoutChange -= handler;
            tcs.TrySetCanceled(cancellationToken);
        });

        Post(RequestLayout);
        await tcs.Task.ConfigureAwait(false);
    }

    private async Task RenderAsync(CancellationToken cancellationToken = default)
    {
        if (_document is null)
        {
            return;
        }

        await WaitForValidSizeAsync(cancellationToken).ConfigureAwait(false);

        if (Width <= 0 || Height <= 0)
        {
            return;
        }

        _renderCancellationTokenSource?.Cancel();
        _renderCancellationTokenSource?.Dispose();
        _renderCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        var token = _renderCancellationTokenSource.Token;
        var previousBitmapLongestSide = Math.Max(_currentBitmapWidth, _currentBitmapHeight);
        var previousRenderedZoom = _lastRenderedZoom;
        var requestedZoom = _zoomFactor;

        try
        {
            var bitmap = await _document.RenderPageAsync(_pageIndex, Width, Height, _zoomFactor, token).ConfigureAwait(false);
            if (token.IsCancellationRequested)
            {
                bitmap.Dispose();
                return;
            }

            Post(() =>
            {
                var previousBitmap = _imageView.Drawable as Android.Graphics.Drawables.BitmapDrawable;
                _imageView.SetImageBitmap(bitmap);
                _currentBitmapWidth = bitmap.Width;
                _currentBitmapHeight = bitmap.Height;
                var newBitmapLongestSide = Math.Max(_currentBitmapWidth, _currentBitmapHeight);
                if (previousBitmapLongestSide > 0 && previousRenderedZoom > 0f)
                {
                    var estimatedRenderedZoom = previousRenderedZoom * (newBitmapLongestSide / (float)previousBitmapLongestSide);
                    _lastRenderedZoom = Math.Clamp(estimatedRenderedZoom, MinZoom, MaxZoom);
                }
                else
                {
                    _lastRenderedZoom = requestedZoom;
                }

                _zoomFactor = _lastRenderedZoom;
                ApplyCenteredImageMatrix(bitmap);
                previousBitmap?.Bitmap?.Dispose();
            });
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void ApplyCenteredImageMatrix(Bitmap bitmap)
    {
        var viewWidth = Width;
        var viewHeight = Height;

        if (viewWidth <= 0 || viewHeight <= 0)
        {
            return;
        }

        ApplyGesturePreviewMatrix();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _gestureRenderCts?.Cancel();
            _gestureRenderCts?.Dispose();
            _gestureRenderCts = null;

            _renderCancellationTokenSource?.Cancel();
            _renderCancellationTokenSource?.Dispose();
            _renderCancellationTokenSource = null;

            _imageMatrix?.Dispose();
            _imageMatrix = null;
        }

        base.Dispose(disposing);
    }

    private sealed class ZoomScaleListener : ScaleGestureDetector.SimpleOnScaleGestureListener
    {
        private readonly ZoomablePdfPageView _owner;

        public ZoomScaleListener(ZoomablePdfPageView owner)
        {
            _owner = owner;
        }

        public override bool OnScaleBegin(ScaleGestureDetector detector)
        {
            _owner._isScaling = true;
            _owner._gestureFocusX = detector.FocusX;
            _owner._gestureFocusY = detector.FocusY;
            _owner.Parent?.RequestDisallowInterceptTouchEvent(true);
            return true;
        }

        public override bool OnScale(ScaleGestureDetector detector)
        {
            _owner.UpdateZoomFromGesture(detector.ScaleFactor, detector.FocusX, detector.FocusY);
            return true;
        }

        public override void OnScaleEnd(ScaleGestureDetector detector)
        {
            _owner._isScaling = false;
            _owner.Parent?.RequestDisallowInterceptTouchEvent(false);
            _owner.NotifyZoomFactorChanged();
            _ = _owner.RenderAsync();
            base.OnScaleEnd(detector);
        }
    }

    private sealed class TapGestureListener : GestureDetector.SimpleOnGestureListener
    {
        private readonly ZoomablePdfPageView _owner;

        public TapGestureListener(ZoomablePdfPageView owner)
        {
            _owner = owner;
        }

        public override bool OnDown(MotionEvent e)
        {
            return true;
        }

        public override bool OnDoubleTap(MotionEvent e)
        {
            _owner.ToggleZoomAt(e.GetX(), e.GetY());
            return true;
        }
    }
}
