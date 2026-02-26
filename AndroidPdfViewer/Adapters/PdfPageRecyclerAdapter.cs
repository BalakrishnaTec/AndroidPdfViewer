using Android.Views;
using AndroidX.RecyclerView.Widget;
using System;
using System.Threading;

namespace AndroidPdfViewer;

public sealed class PdfPageRecyclerAdapter : RecyclerView.Adapter
{
    private readonly PdfDocument _document;
    private readonly float _minZoom;
    private readonly float _maxZoom;
    private readonly bool _synchronizePan;
    private float _sharedZoom;
    private float _sharedPanX;
    private float _sharedPanY;

    public PdfPageRecyclerAdapter(PdfDocument document, float minZoom = 1f, float maxZoom = 6f, bool synchronizePan = false)
    {
        _document = document ?? throw new ArgumentNullException(nameof(document));
        _minZoom = minZoom;
        _maxZoom = maxZoom;
        _synchronizePan = synchronizePan;
        _sharedZoom = Math.Clamp(minZoom, minZoom, maxZoom);
        _sharedPanX = 0f;
        _sharedPanY = 0f;
    }

    public override int ItemCount => _document?.PageCount ?? 0;

    public override RecyclerView.ViewHolder OnCreateViewHolder(ViewGroup parent, int viewType)
    {
        var context = parent.Context ?? throw new InvalidOperationException("RecyclerView parent context is not available.");

        var pageView = new ZoomablePdfPageView(context)
        {
            MinZoom = _minZoom,
            MaxZoom = _maxZoom,
            LayoutParameters = new RecyclerView.LayoutParams(
                ViewGroup.LayoutParams.MatchParent,
                ViewGroup.LayoutParams.MatchParent)
        };

        return new PdfPageViewHolder(pageView);
    }

    public override void OnBindViewHolder(RecyclerView.ViewHolder holder, int position)
    {
        if (holder is not PdfPageViewHolder pageHolder)
        {
            return;
        }

        var panX = _synchronizePan ? _sharedPanX : 0f;
        var panY = _synchronizePan ? _sharedPanY : 0f;
        pageHolder.Bind(_document, position, _sharedZoom, panX, panY, OnPageZoomChanged, OnPageViewStateChanged);
    }

    public override void OnViewRecycled(Java.Lang.Object holder)
    {
        if (holder is PdfPageViewHolder pageHolder)
        {
            pageHolder.Cancel();
        }

        base.OnViewRecycled(holder);
    }

    private void OnPageZoomChanged(float zoom)
    {
        var clamped = Math.Clamp(zoom, _minZoom, _maxZoom);
        if (Math.Abs(_sharedZoom - clamped) < 0.01f)
        {
            return;
        }

        _sharedZoom = clamped;
    }

    private void OnPageViewStateChanged(float zoom, float panX, float panY)
    {
        var clampedZoom = Math.Clamp(zoom, _minZoom, _maxZoom);
        _sharedZoom = clampedZoom;

        if (!_synchronizePan)
        {
            return;
        }

        _sharedPanX = panX;
        _sharedPanY = panY;
    }
 }
