using System;
using System.Threading;
using AndroidX.RecyclerView.Widget;

namespace AndroidPdfViewer;

public sealed class PdfPageViewHolder : RecyclerView.ViewHolder
{
    private readonly ZoomablePdfPageView _pageView;
    private CancellationTokenSource? _bindCts;

    private Action<float>? _zoomChangedCallback;
    private Action<float, float, float>? _viewStateChangedCallback;

    public PdfPageViewHolder(ZoomablePdfPageView pageView) : base(pageView)
    {
        _pageView = pageView;
    }

    public async void Bind(
        PdfDocument document,
        int pageIndex,
        float sharedZoom,
        float sharedPanX,
        float sharedPanY,
        Action<float> zoomChangedCallback,
        Action<float, float, float> viewStateChangedCallback)
    {
        _bindCts?.Cancel();
        _bindCts?.Dispose();
        _bindCts = new CancellationTokenSource();

        if (_zoomChangedCallback is not null)
        {
            _pageView.ZoomFactorChanged -= _zoomChangedCallback;
        }

        _zoomChangedCallback = zoomChangedCallback;
        _pageView.ZoomFactorChanged += _zoomChangedCallback;

        if (_viewStateChangedCallback is not null)
        {
            _pageView.ViewStateChanged -= _viewStateChangedCallback;
        }

        _viewStateChangedCallback = viewStateChangedCallback;
        _pageView.ViewStateChanged += _viewStateChangedCallback;

        _pageView.SetViewState(sharedZoom, sharedPanX, sharedPanY);

        try
        {
            await _pageView.LoadPageAsync(document, pageIndex, _bindCts.Token);
        }
        catch (OperationCanceledException)
        {
        }
    }

    public void Cancel()
    {
        _bindCts?.Cancel();
        _bindCts?.Dispose();
        _bindCts = null;

        if (_zoomChangedCallback is not null)
        {
            _pageView.ZoomFactorChanged -= _zoomChangedCallback;
            _zoomChangedCallback = null;
        }

        if (_viewStateChangedCallback is not null)
        {
            _pageView.ViewStateChanged -= _viewStateChangedCallback;
            _viewStateChangedCallback = null;
        }
    }
}
