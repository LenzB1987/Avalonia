using System;
using System.Collections.Generic;
using System.Numerics;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Rendering;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Utilities;
using Avalonia.Media.Imaging;
using SharpDX;
using SharpDX.Direct2D1;
using SharpDX.Mathematics.Interop;
using BitmapInterpolationMode = Avalonia.Media.Imaging.BitmapInterpolationMode;

namespace Avalonia.Direct2D1.Media
{
    /// <summary>
    /// Draws using Direct2D1.
    /// </summary>
    internal class DrawingContextImpl : IDrawingContextImpl
    {
        private readonly ILayerFactory _layerFactory;
        private readonly SharpDX.Direct2D1.RenderTarget _renderTarget;
        private readonly DeviceContext _deviceContext;
        private readonly bool _ownsDeviceContext;
        private readonly SharpDX.DXGI.SwapChain1 _swapChain;
        private readonly Action _finishedCallback;

        /// <summary>
        /// Initializes a new instance of the <see cref="DrawingContextImpl"/> class.
        /// </summary>
        /// <param name="visualBrushRenderer">The visual brush renderer.</param>
        /// <param name="renderTarget">The render target to draw to.</param>
        /// <param name="layerFactory">
        /// An object to use to create layers. May be null, in which case a
        /// <see cref="WicRenderTargetBitmapImpl"/> will created when a new layer is requested.
        /// </param>
        /// <param name="swapChain">An optional swap chain associated with this drawing context.</param>
        /// <param name="finishedCallback">An optional delegate to be called when context is disposed.</param>
        public DrawingContextImpl(
            ILayerFactory layerFactory,
            SharpDX.Direct2D1.RenderTarget renderTarget,
            SharpDX.DXGI.SwapChain1 swapChain = null,
            Action finishedCallback = null)
        {
            _layerFactory = layerFactory;
            _renderTarget = renderTarget;
            _swapChain = swapChain;
            _finishedCallback = finishedCallback;

            if (_renderTarget is DeviceContext deviceContext)
            {
                _deviceContext = deviceContext;
                _ownsDeviceContext = false;
            }
            else
            {
                _deviceContext = _renderTarget.QueryInterface<DeviceContext>();
                _ownsDeviceContext = true;
            }

            _deviceContext.BeginDraw();
        }

        /// <summary>
        /// Gets the current transform of the drawing context.
        /// </summary>
        public Matrix Transform
        {
            get { return _deviceContext.Transform.ToAvalonia(); }
            set { _deviceContext.Transform = value.ToDirect2D(); }
        }

        public Matrix4x4 Transform4x4
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        /// <inheritdoc/>
        public void Clear(Color color)
        {
            _deviceContext.Clear(color.ToDirect2D());
        }

        /// <summary>
        /// Ends a draw operation.
        /// </summary>
        public void Dispose()
        {
            foreach (var layer in _layerPool)
            {
                layer.Dispose();
            }

            try
            {
                _deviceContext.EndDraw();

                _swapChain?.Present(1, SharpDX.DXGI.PresentFlags.None);
                _finishedCallback?.Invoke();
            }
            catch (SharpDXException ex) when ((uint)ex.HResult == 0x8899000C) // D2DERR_RECREATE_TARGET
            {
                throw new RenderTargetCorruptedException(ex);
            }
            finally
            {
                if (_ownsDeviceContext)
                {
                    _deviceContext.Dispose();
                }
            }
        }

        /// <summary>
        /// Draws a bitmap image.
        /// </summary>
        /// <param name="source">The bitmap image.</param>
        /// <param name="opacity">The opacity to draw with.</param>
        /// <param name="sourceRect">The rect in the image to draw.</param>
        /// <param name="destRect">The rect in the output to draw to.</param>
        /// <param name="bitmapInterpolationMode">The bitmap interpolation mode.</param>
        public void DrawBitmap(IRef<IBitmapImpl> source, double opacity, Rect sourceRect, Rect destRect, BitmapInterpolationMode bitmapInterpolationMode)
        {
            using (var d2d = ((BitmapImpl)source.Item).GetDirect2DBitmap(_deviceContext))
            {
                var interpolationMode = GetInterpolationMode(bitmapInterpolationMode);
                
                // TODO: How to implement CompositeMode here?
                
                _deviceContext.DrawBitmap(
                    d2d.Value,
                    destRect.ToSharpDX(),
                    (float)opacity,
                    interpolationMode,
                    sourceRect.ToSharpDX(),
                    null);
            }
        }

        private static InterpolationMode GetInterpolationMode(BitmapInterpolationMode interpolationMode)
        {
            switch (interpolationMode)
            {
                case BitmapInterpolationMode.LowQuality:
                    return InterpolationMode.NearestNeighbor;
                case BitmapInterpolationMode.MediumQuality:
                    return InterpolationMode.Linear;
                case BitmapInterpolationMode.HighQuality:
                    return InterpolationMode.HighQualityCubic;
                case BitmapInterpolationMode.Default:
                    return InterpolationMode.Linear;
                default:
                    throw new ArgumentOutOfRangeException(nameof(interpolationMode), interpolationMode, null);
            }
        }

        public static CompositeMode GetCompositeMode(BitmapBlendingMode blendingMode)
        {
            switch (blendingMode)
            {  
                case BitmapBlendingMode.SourceIn:
                    return CompositeMode.SourceIn;
                case BitmapBlendingMode.SourceOut:
                    return CompositeMode.SourceOut;
                case BitmapBlendingMode.SourceOver:
                    return CompositeMode.SourceOver;
                case BitmapBlendingMode.SourceAtop:
                    return CompositeMode.SourceAtop; 
                case BitmapBlendingMode.DestinationIn:
                    return CompositeMode.DestinationIn;
                case BitmapBlendingMode.DestinationOut:
                    return CompositeMode.DestinationOut;
                case BitmapBlendingMode.DestinationOver:
                    return CompositeMode.DestinationOver;
                case BitmapBlendingMode.DestinationAtop:
                    return CompositeMode.DestinationAtop;
                case BitmapBlendingMode.Xor:
                    return CompositeMode.Xor;
                case BitmapBlendingMode.Plus:
                    return CompositeMode.Plus;
                default:
                    throw new ArgumentOutOfRangeException(nameof(blendingMode), blendingMode, null);
            }
        }

        /// <summary>
        /// Draws a bitmap image.
        /// </summary>
        /// <param name="source">The bitmap image.</param>
        /// <param name="opacityMask">The opacity mask to draw with.</param>
        /// <param name="opacityMaskRect">The destination rect for the opacity mask.</param>
        /// <param name="destRect">The rect in the output to draw to.</param>
        public void DrawBitmap(IRef<IBitmapImpl> source, IBrush opacityMask, Rect opacityMaskRect, Rect destRect)
        {
            using (var d2dSource = ((BitmapImpl)source.Item).GetDirect2DBitmap(_deviceContext))
            using (var sourceBrush = new BitmapBrush(_deviceContext, d2dSource.Value))
            using (var d2dOpacityMask = CreateBrush(opacityMask, opacityMaskRect.Size))
            using (var geometry = new SharpDX.Direct2D1.RectangleGeometry(Direct2D1Platform.Direct2D1Factory, destRect.ToDirect2D()))
            {
                if (d2dOpacityMask.PlatformBrush != null)
                {
                    d2dOpacityMask.PlatformBrush.Transform = Matrix.CreateTranslation(opacityMaskRect.Position).ToDirect2D();
                }

                _deviceContext.FillGeometry(
                    geometry,
                    sourceBrush,
                    d2dOpacityMask.PlatformBrush);
            }
        }

        /// <summary>
        /// Draws a line.
        /// </summary>
        /// <param name="pen">The stroke pen.</param>
        /// <param name="p1">The first point of the line.</param>
        /// <param name="p2">The second point of the line.</param>
        public void DrawLine(IPen pen, Point p1, Point p2)
        {
            if (pen != null)
            {
                var size = new Rect(p1, p2).Size;

                using (var d2dBrush = CreateBrush(pen.Brush, size))
                using (var d2dStroke = pen.ToDirect2DStrokeStyle(_deviceContext))
                {
                    if (d2dBrush.PlatformBrush != null)
                    {
                        _deviceContext.DrawLine(
                            p1.ToSharpDX(),
                            p2.ToSharpDX(),
                            d2dBrush.PlatformBrush,
                            (float)pen.Thickness,
                            d2dStroke);
                    }
                }
            }
        }

        /// <summary>
        /// Draws a geometry.
        /// </summary>
        /// <param name="brush">The fill brush.</param>
        /// <param name="pen">The stroke pen.</param>
        /// <param name="geometry">The geometry.</param>
        public void DrawGeometry(IBrush brush, IPen pen, IGeometryImpl geometry)
        {
            if (brush != null)
            {
                using (var d2dBrush = CreateBrush(brush, geometry.Bounds.Size))
                {
                    if (d2dBrush.PlatformBrush != null)
                    {
                        var impl = (GeometryImpl)geometry;
                        _deviceContext.FillGeometry(impl.Geometry, d2dBrush.PlatformBrush);
                    }
                }
            }

            if (pen != null)
            {
                using (var d2dBrush = CreateBrush(pen.Brush, geometry.GetRenderBounds(pen).Size))
                using (var d2dStroke = pen.ToDirect2DStrokeStyle(_deviceContext))
                {
                    if (d2dBrush.PlatformBrush != null)
                    {
                        var impl = (GeometryImpl)geometry;
                        _deviceContext.DrawGeometry(impl.Geometry, d2dBrush.PlatformBrush, (float)pen.Thickness, d2dStroke);
                    }
                }
            }
        }

        /// <inheritdoc />
        public void DrawRectangle(IBrush brush, IPen pen, RoundedRect rrect, BoxShadows boxShadow = default)
        {
            var rc = rrect.Rect.ToDirect2D();
            var rect = rrect.Rect;
            var radiusX = Math.Max(rrect.RadiiTopLeft.X,
                Math.Max(rrect.RadiiTopRight.X, Math.Max(rrect.RadiiBottomRight.X, rrect.RadiiBottomLeft.X)));
            var radiusY = Math.Max(rrect.RadiiTopLeft.Y,
                Math.Max(rrect.RadiiTopRight.Y, Math.Max(rrect.RadiiBottomRight.Y, rrect.RadiiBottomLeft.Y)));
            var isRounded = !MathUtilities.IsZero(radiusX) || !MathUtilities.IsZero(radiusY);

            if (brush != null)
            {
                using (var b = CreateBrush(brush, rect.Size))
                {
                    if (b.PlatformBrush != null)
                    {
                        if (isRounded)
                        {
                            _deviceContext.FillRoundedRectangle(
                                new RoundedRectangle
                                {
                                    Rect = new RawRectangleF(
                                        (float)rect.X,
                                        (float)rect.Y,
                                        (float)rect.Right,
                                        (float)rect.Bottom),
                                    RadiusX = (float)radiusX,
                                    RadiusY = (float)radiusY
                                },
                                b.PlatformBrush);
                        }
                        else
                        {
                            _deviceContext.FillRectangle(rc, b.PlatformBrush);
                        }
                    }
                }
            }

            if (pen?.Brush != null)
            {
                using (var wrapper = CreateBrush(pen.Brush, rect.Size))
                using (var d2dStroke = pen.ToDirect2DStrokeStyle(_deviceContext))
                {
                    if (wrapper.PlatformBrush != null)
                    {
                        if (isRounded)
                        {
                            _deviceContext.DrawRoundedRectangle(
                                new RoundedRectangle { Rect = rc, RadiusX = (float)radiusX, RadiusY = (float)radiusY },
                                wrapper.PlatformBrush,
                                (float)pen.Thickness,
                                d2dStroke);
                        }
                        else
                        {
                            _deviceContext.DrawRectangle(
                                rc,
                                wrapper.PlatformBrush,
                                (float)pen.Thickness,
                                d2dStroke);
                        }
                    }
                }
            }
        }

        /// <inheritdoc />
        public void DrawEllipse(IBrush brush, IPen pen, Rect rect)
        {
            var rc = rect.ToDirect2D();

            if (brush != null)
            {
                using (var b = CreateBrush(brush, rect.Size))
                {
                    if (b.PlatformBrush != null)
                    {
                        _deviceContext.FillEllipse(new Ellipse
                        {
                            Point = rect.Center.ToSharpDX(),
                            RadiusX = (float)(rect.Width / 2),
                            RadiusY = (float)(rect.Height / 2)
                        }, b.PlatformBrush);
                    }
                }
            }

            if (pen?.Brush != null)
            {
                using (var wrapper = CreateBrush(pen.Brush, rect.Size))
                using (var d2dStroke = pen.ToDirect2DStrokeStyle(_deviceContext))
                {
                    if (wrapper.PlatformBrush != null)
                    {
                        _deviceContext.DrawEllipse(new Ellipse
                        {
                            Point = rect.Center.ToSharpDX(),
                            RadiusX = (float)(rect.Width / 2),
                            RadiusY = (float)(rect.Height / 2)
                        }, wrapper.PlatformBrush, (float)pen.Thickness, d2dStroke);
                    }
                }
            }
        }

        /// <summary>
        /// Draws a glyph run.
        /// </summary>
        /// <param name="foreground">The foreground.</param>
        /// <param name="glyphRun">The glyph run.</param>
        public void DrawGlyphRun(IBrush foreground, IRef<IGlyphRunImpl> glyphRun)
        {
            using (var brush = CreateBrush(foreground, glyphRun.Item.Size))
            {
                var glyphRunImpl = (GlyphRunImpl)glyphRun.Item;

                _renderTarget.DrawGlyphRun(glyphRun.Item.BaselineOrigin.ToSharpDX(), glyphRunImpl.GlyphRun,
                    brush.PlatformBrush, MeasuringMode.Natural);
            }
        }

        public IDrawingContextLayerImpl CreateLayer(Size size)
        {
            if (_layerFactory != null)
            {
                return _layerFactory.CreateLayer(size);
            }
            else
            {
                var platform = AvaloniaLocator.Current.GetRequiredService<IPlatformRenderInterface>();
                var dpi = new Vector(_deviceContext.DotsPerInch.Width, _deviceContext.DotsPerInch.Height);
                var pixelSize = PixelSize.FromSizeWithDpi(size, dpi);
                return (IDrawingContextLayerImpl)platform.CreateRenderTargetBitmap(pixelSize, dpi);
            }
        }

        /// <summary>
        /// Pushes a clip rectange.
        /// </summary>
        /// <param name="clip">The clip rectangle.</param>
        /// <returns>A disposable used to undo the clip rectangle.</returns>
        public void PushClip(Rect clip)
        {
            _deviceContext.PushAxisAlignedClip(clip.ToSharpDX(), AntialiasMode.PerPrimitive);
        }

        public void PushClip(RoundedRect clip)
        {
            //TODO: radius
            _deviceContext.PushAxisAlignedClip(clip.Rect.ToDirect2D(), AntialiasMode.PerPrimitive);
        }

        public void PopClip()
        {
            _deviceContext.PopAxisAlignedClip();
        }

        readonly Stack<Layer> _layers = new Stack<Layer>();
        private readonly Stack<Layer> _layerPool = new Stack<Layer>();
        /// <summary>
        /// Pushes an opacity value.
        /// </summary>
        /// <param name="opacity">The opacity.</param>
        /// <param name="bounds">The bounds.</param>
        /// <returns>A disposable used to undo the opacity.</returns>
        public void PushOpacity(double opacity, Rect bounds)
        {
            if (opacity < 1)
            {
                var parameters = new LayerParameters
                {
                    ContentBounds = bounds.ToDirect2D(),
                    MaskTransform = PrimitiveExtensions.Matrix3x2Identity,
                    Opacity = (float)opacity,
                };

                var layer = _layerPool.Count != 0 ? _layerPool.Pop() : new Layer(_deviceContext);
                _deviceContext.PushLayer(ref parameters, layer);

                _layers.Push(layer);
            }
            else
                _layers.Push(null);
        }

        public void PopOpacity()
        {
            PopLayer();
        }

        private void PopLayer()
        {
            var layer = _layers.Pop();
            if (layer != null)
            {
                _deviceContext.PopLayer();
                _layerPool.Push(layer);
            }
        }

        /// <summary>
        /// Creates a Direct2D brush wrapper for a Avalonia brush.
        /// </summary>
        /// <param name="brush">The avalonia brush.</param>
        /// <param name="destinationSize">The size of the brush's target area.</param>
        /// <returns>The Direct2D brush wrapper.</returns>
        public BrushImpl CreateBrush(IBrush brush, Size destinationSize)
        {
            var solidColorBrush = brush as ISolidColorBrush;
            var linearGradientBrush = brush as ILinearGradientBrush;
            var radialGradientBrush = brush as IRadialGradientBrush;
            var conicGradientBrush = brush as IConicGradientBrush;
            var imageBrush = brush as IImageBrush;
            var sceneBrush = brush as ISceneBrush;
            var sceneBrushContent = brush as ISceneBrushContent;

            if (solidColorBrush != null)
            {
                return new SolidColorBrushImpl(solidColorBrush, _deviceContext);
            }
            else if (linearGradientBrush != null)
            {
                return new LinearGradientBrushImpl(linearGradientBrush, _deviceContext, destinationSize);
            }
            else if (radialGradientBrush != null)
            {
                return new RadialGradientBrushImpl(radialGradientBrush, _deviceContext, destinationSize);
            }
            else if (conicGradientBrush != null)
            {
                // there is no Direct2D implementation of Conic Gradients so use Radial as a stand-in
                return new SolidColorBrushImpl(conicGradientBrush, _deviceContext);
            }
            else if (imageBrush?.Source != null)
            {
                return new ImageBrushImpl(
                    imageBrush,
                    _deviceContext,
                    (BitmapImpl)imageBrush.Source.PlatformImpl.Item,
                    destinationSize);
            }
            else if (sceneBrush != null || sceneBrushContent != null)
            {
                sceneBrushContent ??= sceneBrush.CreateContent();
                if (sceneBrushContent != null)
                {
                    var rect = sceneBrushContent.Rect;
                    var intermediateSize = rect.Size;

                    if (intermediateSize.Width >= 1 && intermediateSize.Height >= 1)
                    {
                        // We need to ensure the size we're requesting is an integer pixel size, otherwise
                        // D2D alters the DPI of the render target, which messes stuff up. PixelSize.FromSize
                        // will do the rounding for us.
                        var dpi = new Vector(_deviceContext.DotsPerInch.Width, _deviceContext.DotsPerInch.Height);
                        var pixelSize = PixelSize.FromSizeWithDpi(intermediateSize, dpi);

                        using (var intermediate = new BitmapRenderTarget(
                                   _deviceContext,
                                   CompatibleRenderTargetOptions.None,
                                   pixelSize.ToSizeWithDpi(dpi).ToSharpDX()))
                        {
                            using (var ctx = new RenderTarget(intermediate).CreateDrawingContext())
                            {
                                intermediate.Clear(null);
                                sceneBrushContent.Render(ctx,
                                    rect.TopLeft == default ? null : Matrix.CreateTranslation(-rect.X, -rect.Y));
                            }

                            return new ImageBrushImpl(
                                sceneBrushContent.Brush,
                                _deviceContext,
                                new D2DBitmapImpl(intermediate.Bitmap),
                                destinationSize);
                        }

                    }
                }
            }

            return new SolidColorBrushImpl(null, _deviceContext);
        }

        public void PushGeometryClip(IGeometryImpl clip)
        {
            var parameters = new LayerParameters
            {
                ContentBounds = PrimitiveExtensions.RectangleInfinite,
                MaskTransform = PrimitiveExtensions.Matrix3x2Identity,
                Opacity = 1,
                GeometricMask = ((GeometryImpl)clip).Geometry,
                MaskAntialiasMode = AntialiasMode.PerPrimitive
            };
            var layer = _layerPool.Count != 0 ? _layerPool.Pop() : new Layer(_deviceContext);
            _deviceContext.PushLayer(ref parameters, layer);

            _layers.Push(layer);

        }

        public void PopGeometryClip()
        {
            PopLayer();
        }

        public void PushBitmapBlendMode(BitmapBlendingMode blendingMode)
        {
            // TODO: Stubs for now
        }

        public void PopBitmapBlendMode()
        {
            // TODO: Stubs for now
        }

        public void PushOpacityMask(IBrush mask, Rect bounds)
        {
            var parameters = new LayerParameters
            {
                ContentBounds = PrimitiveExtensions.RectangleInfinite,
                MaskTransform = PrimitiveExtensions.Matrix3x2Identity,
                Opacity = 1,
                OpacityBrush = CreateBrush(mask, bounds.Size).PlatformBrush
            };
            var layer = _layerPool.Count != 0 ? _layerPool.Pop() : new Layer(_deviceContext);
            _deviceContext.PushLayer(ref parameters, layer);

            _layers.Push(layer);
        }

        public void PopOpacityMask()
        {
            PopLayer();
        }
        
        public void Custom(ICustomDrawOperation custom) => custom.Render(this);
        public object GetFeature(Type t) => null;
    }
}
