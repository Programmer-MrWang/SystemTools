using System;
using System.IO;
using Avalonia;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using SkiaSharp;

namespace LiquidGlassAvaloniaUI
{
    internal class LiquidGlassDrawOperation : ICustomDrawOperation
    {
        private static SKRuntimeEffect? s_lensEffect;
        private static SKRuntimeEffect? s_highlightEffect;
        private static SKRuntimeEffect? s_gammaEffect;
        private static SKRuntimeEffect? s_interactiveHighlightEffect;
        private static SKRuntimeEffect? s_progressiveMaskEffect;
        private static SKRuntimeEffect? s_backdropTransformEffect;
        private static SKRuntimeEffect? s_backdropFilterEffect;
        private static bool s_loaded;

        private readonly Rect _bounds;
        private readonly LiquidGlassDrawParameters _parameters;
        private readonly LiquidGlassBackdropSnapshot? _backdropSnapshot;
        private readonly LiquidGlassDrawPass _pass;

        public LiquidGlassDrawOperation(
            Rect bounds,
            LiquidGlassDrawParameters parameters,
            LiquidGlassBackdropSnapshot? snapshot,
            LiquidGlassDrawPass pass)
        {
            _bounds = bounds;
            _parameters = parameters;
            _backdropSnapshot = snapshot is not null && snapshot.TryAddLease() ? snapshot : null;
            _pass = pass;
        }

        public void Dispose()
        {
            _backdropSnapshot?.ReleaseLease();
        }

        public bool HitTest(Point p)
        {
            return false;
        }

        public Rect Bounds
        {
            get => _bounds;
        }

        public bool Equals(ICustomDrawOperation? other)
        {
            return false;
        }

        public void Render(ImmediateDrawingContext context)
        {
            ISkiaSharpApiLeaseFeature? leaseFeature = context.TryGetFeature<ISkiaSharpApiLeaseFeature>();
            if (leaseFeature is null)
                return;

            LoadShaders();

            using ISkiaSharpApiLease lease = leaseFeature.Lease();
            SKCanvas canvas = lease.SkCanvas;

            switch (_pass)
            {
                case LiquidGlassDrawPass.Lens:
                    RenderLens(canvas);
                    break;
                case LiquidGlassDrawPass.InteractiveHighlight:
                    RenderInteractiveHighlight(canvas);
                    break;
                case LiquidGlassDrawPass.Highlight:
                    RenderHighlight(canvas);
                    break;
            }
        }

        private static void LoadShaders()
        {
            if (s_loaded)
                return;
            s_loaded = true;

            s_lensEffect = LoadRuntimeEffect("avares://SystemTools/ThirdParty/LiquidGlassAvaloniaUI/Assets/Shaders/LiquidGlassShader.sksl");
            s_highlightEffect = LoadRuntimeEffect("avares://SystemTools/ThirdParty/LiquidGlassAvaloniaUI/Assets/Shaders/LiquidGlassHighlight.sksl");
            s_gammaEffect = LoadRuntimeEffect("avares://SystemTools/ThirdParty/LiquidGlassAvaloniaUI/Assets/Shaders/LiquidGlassGamma.sksl");
            s_interactiveHighlightEffect = LoadRuntimeEffect("avares://SystemTools/ThirdParty/LiquidGlassAvaloniaUI/Assets/Shaders/LiquidGlassInteractiveHighlight.sksl");
            s_progressiveMaskEffect = LoadRuntimeEffect("avares://SystemTools/ThirdParty/LiquidGlassAvaloniaUI/Assets/Shaders/LiquidGlassProgressiveMask.sksl");
            s_backdropTransformEffect = LoadRuntimeEffect("avares://SystemTools/ThirdParty/LiquidGlassAvaloniaUI/Assets/Shaders/LiquidGlassBackdropTransform.sksl");
            s_backdropFilterEffect = LoadRuntimeEffect("avares://SystemTools/ThirdParty/LiquidGlassAvaloniaUI/Assets/Shaders/LiquidGlassBackdropFilter.sksl");
        }

        private static SKRuntimeEffect? LoadRuntimeEffect(string assetUriString)
        {
            try
            {
                Uri assetUri = new(assetUriString);
                using Stream stream = AssetLoader.Open(assetUri);
                using StreamReader reader = new(stream);
                string shaderCode = reader.ReadToEnd();

                SKRuntimeEffect? effect = SKRuntimeEffect.CreateShader(shaderCode, out string? errorText);
                if (effect == null)
                    Console.WriteLine($"[LiquidGlass] Failed to create SKRuntimeEffect ({assetUriString}): {errorText}");

                return effect;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[LiquidGlass] Exception while loading shader ({assetUriString}): {ex.Message}");
                return null;
            }
        }

        private void RenderLens(SKCanvas canvas)
        {
            if (s_lensEffect is null)
            {
                DrawErrorHint(canvas);
                return;
            }

            if (_backdropSnapshot is null)
            {
                DrawBackdropNotReady(canvas);
                return;
            }

            SKMatrix currentTransform = canvas.TotalMatrix;
            if (!currentTransform.TryInvert(out SKMatrix currentInvertedTransform))
                return;

            SKSize size = new((float)_bounds.Width, (float)_bounds.Height);
            if (size.Width <= 0 || size.Height <= 0)
                return;

            float maxRadius = Math.Min(size.Width, size.Height) * 0.5f;
            float[] cornerRadii = GetCornerRadii(_parameters.CornerRadius, maxRadius);

            using SKShader? backdropShader = SKShader.CreateImage(
                _backdropSnapshot.Image,
                SKShaderTileMode.Clamp,
                SKShaderTileMode.Clamp,
                WithPixelOrigin(currentInvertedTransform, _backdropSnapshot.OriginInPixels));

            SKShader? lensInput = (SKShader)backdropShader;

            SKShader? backdropFilterShader = null;
            SKShader? backdropTransformShader = null;
            try
            {
                var blurRadius = (float)Clamp(_parameters.BlurRadius, 0.0, 64.0);
                var vibrancy = (float)Clamp(_parameters.Vibrancy, 0.0, 4.0);
                var brightness = (float)Clamp(_parameters.Brightness, -1.0, 1.0);
                var contrast = (float)Clamp(_parameters.Contrast, 0.0, 4.0);
                var exposureEv = (float)Clamp(_parameters.ExposureEv, -4.0, 4.0);
                var backdropOpacity = (float)Clamp(_parameters.BackdropOpacity, 0.0, 1.0);
                var needsBackdropFilter =
                    blurRadius > 0.0005f
                    || Math.Abs(vibrancy - 1.0f) > 0.0005f
                    || Math.Abs(brightness) > 0.0005f
                    || Math.Abs(contrast - 1.0f) > 0.0005f
                    || Math.Abs(exposureEv) > 0.0005f
                    || Math.Abs(backdropOpacity - 1.0f) > 0.0005f;

                if (needsBackdropFilter && s_backdropFilterEffect is not null)
                {
                    using SKRuntimeEffectUniforms uniforms = new(s_backdropFilterEffect);
                    uniforms["blurRadius"] = blurRadius;
                    uniforms["vibrancy"] = vibrancy;
                    uniforms["brightness"] = brightness;
                    uniforms["contrast"] = contrast;
                    uniforms["exposureEv"] = exposureEv;
                    uniforms["opacity"] = backdropOpacity;

                    using SKRuntimeEffectChildren children = new(s_backdropFilterEffect);
                    children["content"] = lensInput;
                    backdropFilterShader = s_backdropFilterEffect.ToShader(uniforms, children);
                    if (backdropFilterShader is not null)
                        lensInput = backdropFilterShader;
                }

                double zoomValue = _parameters.BackdropZoom;
                if (zoomValue <= 0.0005 || double.IsNaN(zoomValue) || double.IsInfinity(zoomValue))
                    zoomValue = 1.0;

                float zoom = (float)Clamp(zoomValue, 0.1, 10.0);
                Vector offset = _parameters.BackdropOffset;
                bool needsTransform =
                    Math.Abs(zoom - 1.0f) > 0.0005f
                    || Math.Abs(offset.X) > 0.0005
                    || Math.Abs(offset.Y) > 0.0005;

                if (needsTransform && s_backdropTransformEffect is not null)
                {
                    using SKRuntimeEffectUniforms uniforms = new(s_backdropTransformEffect);
                    uniforms["size"] = new[]
                    {
                        size.Width, size.Height
                    };
                    uniforms["zoom"] = zoom;
                    uniforms["offset"] = new[]
                    {
                        (float)offset.X, (float)offset.Y
                    };

                    using SKRuntimeEffectChildren children = new(s_backdropTransformEffect);
                    children["content"] = lensInput;

                    backdropTransformShader = s_backdropTransformEffect.ToShader(uniforms, children);
                    if (backdropTransformShader is not null)
                        lensInput = backdropTransformShader;
                }

                float refractionHeight = (float)Clamp(_parameters.RefractionHeight, 0.0, Math.Min(size.Width, size.Height) * 0.5);
                float refractionAmount = (float)_parameters.RefractionAmount;
                bool applyLens = refractionHeight > 0.001f && Math.Abs(refractionAmount) > 0.001f;

                SKShader? lensShader = null;
                if (applyLens)
                {
                    using SKRuntimeEffectUniforms lensUniforms = new(s_lensEffect);
                    lensUniforms["size"] = new[]
                    {
                        size.Width, size.Height
                    };
                    lensUniforms["cornerRadii"] = cornerRadii;
                    lensUniforms["refractionHeight"] = refractionHeight;
                    // The lens shader expects a negative refraction amount.
                    lensUniforms["refractionAmount"] = -refractionAmount;
                    lensUniforms["depthEffect"] = _parameters.DepthEffect ? 1.0f : 0.0f;
                    lensUniforms["chromaticAberration"] = _parameters.ChromaticAberration ? 1.0f : 0.0f;

                    using SKRuntimeEffectChildren lensChildren = new(s_lensEffect);
                    lensChildren["content"] = lensInput;

                    lensShader = s_lensEffect.ToShader(lensUniforms, lensChildren);
                }

                SKShader? baseShader = lensShader ?? lensInput;

                SKShader? progressiveMaskShader = null;
                try
                {
                    if (_parameters.ProgressiveBlurEnabled
                        && s_progressiveMaskEffect is not null)
                    {
                        using SKRuntimeEffectUniforms uniforms = new(s_progressiveMaskEffect);
                        uniforms["size"] = new[]
                        {
                            size.Width, size.Height
                        };
                        uniforms["start"] = (float)Clamp(_parameters.ProgressiveBlurStart, 0.0, 1.0);
                        uniforms["end"] = (float)Clamp(_parameters.ProgressiveBlurEnd, 0.0, 1.0);

                        Color tint = _parameters.ProgressiveTintColor;
                        uniforms["tint"] = new[]
                        {
                            tint.R / 255f, tint.G / 255f, tint.B / 255f, tint.A / 255f
                        };

                        float tintIntensity = tint.A > 0
                            ? (float)Clamp(_parameters.ProgressiveTintIntensity, 0.0, 1.0)
                            : 0.0f;
                        uniforms["tintIntensity"] = tintIntensity;

                        using SKRuntimeEffectChildren children = new(s_progressiveMaskEffect);
                        children["content"] = baseShader;

                        progressiveMaskShader = s_progressiveMaskEffect.ToShader(uniforms, children);
                        if (progressiveMaskShader is not null)
                            baseShader = progressiveMaskShader;
                    }

                    SKShader? gammaShader = null;
                    try
                    {
                        float gammaPower = (float)Clamp(_parameters.GammaPower, 0.0, 10.0);
                        if (s_gammaEffect is not null && Math.Abs(gammaPower - 1.0f) > 0.0005f)
                        {
                            using SKRuntimeEffectUniforms uniforms = new(s_gammaEffect);
                            uniforms["power"] = gammaPower;
                            using SKRuntimeEffectChildren children = new(s_gammaEffect);
                            children["content"] = baseShader;
                            gammaShader = s_gammaEffect.ToShader(uniforms, children);
                        }

                        using SKPaint paint = new()
                        {
                            Shader = gammaShader ?? baseShader,
                            IsAntialias = true
                        };

                        SKRect rect = SKRect.Create(0, 0, size.Width, size.Height);
                        using SKPath clipPath = CreateRoundRectPath(rect, cornerRadii);

                        canvas.Save();
                        canvas.ClipPath(clipPath, SKClipOperation.Intersect, true);
                        canvas.DrawRect(rect, paint);

                        DrawSurfaceOverlay(canvas, rect);

                        canvas.Restore();
                    }
                    finally
                    {
                        gammaShader?.Dispose();
                    }
                }
                finally
                {
                    progressiveMaskShader?.Dispose();
                    lensShader?.Dispose();
                }
            }
            finally
            {
                backdropTransformShader?.Dispose();
                backdropFilterShader?.Dispose();
            }

        }

        private void RenderInteractiveHighlight(SKCanvas canvas)
        {
            if (s_interactiveHighlightEffect is null)
                return;

            float progress = (float)Clamp(_parameters.InteractiveProgress, 0.0, 1.0);
            if (progress <= 0.001f)
                return;

            SKSize size = new((float)_bounds.Width, (float)_bounds.Height);
            if (size.Width <= 0 || size.Height <= 0)
                return;

            float maxRadius = Math.Min(size.Width, size.Height) * 0.5f;
            float[] cornerRadii = GetCornerRadii(_parameters.CornerRadius, maxRadius);

            SKRect rect = SKRect.Create(0, 0, size.Width, size.Height);
            using SKPath clipPath = CreateRoundRectPath(rect, cornerRadii);

            canvas.Save();
            canvas.ClipPath(clipPath, SKClipOperation.Intersect, true);

            using (SKPaint basePaint = new()
                {
                    Color = new SKColor(255, 255, 255, (byte)Clamp(0.08f * progress * 255f, 0f, 255f)),
                    BlendMode = SKBlendMode.Plus,
                    IsAntialias = true
                })
            {
                canvas.DrawRect(rect, basePaint);
            }

            using SKRuntimeEffectUniforms uniforms = new(s_interactiveHighlightEffect);
            uniforms["size"] = new[]
            {
                size.Width, size.Height
            };
            uniforms["color"] = new[]
            {
                1.0f, 1.0f, 1.0f, (float)Clamp(0.15 * progress, 0.0, 1.0)
            };
            uniforms["radius"] = Math.Min(size.Width, size.Height) * 1.5f;
            uniforms["position"] = new[]
            {
                (float)Clamp(_parameters.InteractivePosition.X, 0.0, size.Width), (float)Clamp(_parameters.InteractivePosition.Y, 0.0, size.Height)
            };

            using SKRuntimeEffectChildren children = new(s_interactiveHighlightEffect);
            using SKShader? shader = s_interactiveHighlightEffect.ToShader(uniforms, children);

            if (shader is not null)
            {
                using SKPaint paint = new()
                {
                    Shader = shader,
                    BlendMode = SKBlendMode.Plus,
                    IsAntialias = true
                };
                canvas.DrawRect(rect, paint);
            }

            canvas.Restore();
        }

        private void RenderHighlight(SKCanvas canvas)
        {
            if (s_highlightEffect is null)
                return;

            if (_parameters.HighlightOpacity <= 0.001 || _parameters.HighlightWidth <= 0.001)
                return;

            SKSize size = new((float)_bounds.Width, (float)_bounds.Height);
            if (size.Width <= 0 || size.Height <= 0)
                return;

            float maxRadius = Math.Min(size.Width, size.Height) * 0.5f;
            float[] cornerRadii = GetCornerRadii(_parameters.CornerRadius, maxRadius);

            using SKRuntimeEffectUniforms uniforms = new(s_highlightEffect);
            uniforms["size"] = new[]
            {
                size.Width, size.Height
            };
            uniforms["cornerRadii"] = cornerRadii;

            float alpha = (float)Clamp(_parameters.HighlightOpacity, 0.0, 1.0);
            uniforms["color"] = new[]
            {
                1.0f, 1.0f, 1.0f, alpha
            };

            float angleRad = (float)(_parameters.HighlightAngleDegrees * (Math.PI / 180.0));
            uniforms["angle"] = angleRad;
            uniforms["falloff"] = (float)Clamp(_parameters.HighlightFalloff, 0.0, 8.0);

            using SKRuntimeEffectChildren children = new(s_highlightEffect);
            using SKShader? shader = s_highlightEffect.ToShader(uniforms, children);
            if (shader is null)
                return;

            float blurRadius = (float)Clamp(_parameters.HighlightBlurRadius, 0.0, 20.0);
            using SKMaskFilter? maskFilter = blurRadius > 0.001f
                ? SKMaskFilter.CreateBlur(SKBlurStyle.Normal, blurRadius)
                : null;

            float strokeWidth = (float)(Math.Ceiling(Clamp(_parameters.HighlightWidth, 0.0, 100.0)) * 2.0);

            using SKPaint paint = new()
            {
                Shader = shader,
                IsAntialias = true,
                BlendMode = SKBlendMode.Plus,
                Style = SKPaintStyle.Stroke,
                StrokeJoin = SKStrokeJoin.Round,
                StrokeCap = SKStrokeCap.Round,
                StrokeWidth = Math.Max(0.5f, strokeWidth),
                MaskFilter = maskFilter
            };

            SKRect rect = SKRect.Create(0, 0, size.Width, size.Height);
            using SKPath path = CreateRoundRectPath(rect, cornerRadii);

            // Pad the highlight layer to avoid edge artifacts when transformed and/or rasterized into an intermediate surface.
            const float safePad = 1.0f;

            canvas.Save();
            canvas.Translate(-safePad, -safePad);
            SKRect layerBounds = SKRect.Create(0, 0, size.Width + safePad * 2.0f, size.Height + safePad * 2.0f);
            canvas.SaveLayer(layerBounds, null);

            canvas.Translate(safePad, safePad);
            canvas.ClipPath(path, SKClipOperation.Intersect, true);
            canvas.DrawPath(path, paint);

            canvas.Restore();
            canvas.Restore();
        }

        private void DrawSurfaceOverlay(SKCanvas canvas, SKRect rect)
        {
            // Optional tint/surface overlays. If TintColor is specified, it draws it twice: Hue blend + alpha fill.
            if (_parameters.TintColor.A > 0)
            {
                Color tint = _parameters.TintColor;
                using SKPaint huePaint = new()
                {
                    Color = new SKColor(tint.R, tint.G, tint.B, 255),
                    IsAntialias = true,
                    BlendMode = SKBlendMode.Hue
                };
                canvas.DrawRect(rect, huePaint);

                using SKPaint fillPaint = new()
                {
                    Color = new SKColor(tint.R, tint.G, tint.B, (byte)Clamp(tint.A * 0.75, 0.0, 255.0)),
                    IsAntialias = true,
                    BlendMode = SKBlendMode.SrcOver
                };
                canvas.DrawRect(rect, fillPaint);
            }

            if (_parameters.SurfaceColor.A > 0)
            {
                Color surface = _parameters.SurfaceColor;
                using SKPaint paint = new()
                {
                    Color = new SKColor(surface.R, surface.G, surface.B, surface.A),
                    IsAntialias = true,
                    BlendMode = SKBlendMode.SrcOver
                };
                canvas.DrawRect(rect, paint);
            }
        }

        private static float[] GetCornerRadii(CornerRadius cornerRadius, float maxRadius)
        {
            float tl = (float)Clamp(cornerRadius.TopLeft, 0.0, maxRadius);
            float tr = (float)Clamp(cornerRadius.TopRight, 0.0, maxRadius);
            float br = (float)Clamp(cornerRadius.BottomRight, 0.0, maxRadius);
            float bl = (float)Clamp(cornerRadius.BottomLeft, 0.0, maxRadius);
            return new[]
            {
                tl, tr, br, bl
            };
        }

        private static SKPath CreateRoundRectPath(SKRect rect, float[] cornerRadii)
        {
            using SKRoundRect rr = new();
            rr.SetRectRadii(rect, new[]
            {
                new SKPoint(cornerRadii[0], cornerRadii[0]), new SKPoint(cornerRadii[1], cornerRadii[1]), new SKPoint(cornerRadii[2], cornerRadii[2]), new SKPoint(cornerRadii[3], cornerRadii[3])
            });

            SKPath path = new();
            path.AddRoundRect(rr, SKPathDirection.Clockwise);
            return path;
        }

        private void DrawErrorHint(SKCanvas canvas)
        {
            using SKPaint errorPaint = new()
            {
                Color = new SKColor(255, 0, 0, 120),
                Style = SKPaintStyle.Fill
            };

            canvas.DrawRect(SKRect.Create(0, 0, (float)_bounds.Width, (float)_bounds.Height), errorPaint);
        }

        private void DrawBackdropNotReady(SKCanvas canvas)
        {
            SKSize size = new((float)_bounds.Width, (float)_bounds.Height);
            SKRect rect = SKRect.Create(0, 0, size.Width, size.Height);

            float maxRadius = Math.Min(size.Width, size.Height) * 0.5f;
            float[] cornerRadii = GetCornerRadii(_parameters.CornerRadius, maxRadius);
            using SKPath path = CreateRoundRectPath(rect, cornerRadii);

            using SKPaint paint = new()
            {
                Color = new SKColor(255, 255, 255, 32),
                IsAntialias = true
            };

            canvas.Save();
            canvas.ClipPath(path, SKClipOperation.Intersect, true);
            canvas.DrawRect(rect, paint);
            canvas.Restore();
        }

        private static double Clamp(double value, double min, double max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        private static SKMatrix WithPixelOrigin(SKMatrix invertedTransform, PixelPoint originInPixels)
        {
            // localMatrix = invertedTransform * Translate(+originInPixels)
            //
            // We first cancel the current canvas transform (so shader coordinates become device pixels),
            // then shift into the clipped snapshot's coordinate system.
            float ox = (float)originInPixels.X;
            float oy = (float)originInPixels.Y;
            invertedTransform.TransX = invertedTransform.TransX + invertedTransform.ScaleX * ox + invertedTransform.SkewX * oy;
            invertedTransform.TransY = invertedTransform.TransY + invertedTransform.SkewY * ox + invertedTransform.ScaleY * oy;
            return invertedTransform;
        }
    }
}
