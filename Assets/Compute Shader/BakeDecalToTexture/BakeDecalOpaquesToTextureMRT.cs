using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class BakeDecalOpaquesToTextureMRT : ScriptableRendererFeature
{
    public bool Preview;
    MRTDrawPass opaqueMrtWritePass;
    MRTPreviewPass opaqueMrtPreviewPass;
    MRTTextureBlitPass opaqueMrtTextureBlitPass;
    public override void Create()
    {
        opaqueMrtWritePass = new MRTDrawPass();
        opaqueMrtPreviewPass = new MRTPreviewPass();
        opaqueMrtTextureBlitPass = new MRTTextureBlitPass();

        if (opaqueMrtWritePass.onCreatedRT == null)
        {
            opaqueMrtWritePass.onCreatedRT += (c,d,wN,wPddx,wPddy) =>
            {
                if(Preview) opaqueMrtPreviewPass.uvRTPreview = c;
                opaqueMrtTextureBlitPass.uvTextureBlitPass = c;
                opaqueMrtTextureBlitPass.ddxyTextureBlitPass = d;
                opaqueMrtTextureBlitPass.wNormTextureBlitPass = wN;
                opaqueMrtTextureBlitPass.ddxWPosTextureBlitPass = wPddx;
                opaqueMrtTextureBlitPass.ddyWPosTextureBlitPass = wPddy;
            };
        }
    }

    public override void AddRenderPasses(ScriptableRenderer renderer,ref RenderingData renderingData)
    {
        if (renderingData.cameraData.cameraType != CameraType.Game)
            return;

        renderingData.cameraData.camera.allowMSAA = false;
        renderingData.cameraData.requiresDepthTexture = true;

        opaqueMrtWritePass.renderer = renderer;
        opaqueMrtPreviewPass.renderer = renderer;
        opaqueMrtTextureBlitPass.renderer = renderer;

        renderer.EnqueuePass(opaqueMrtWritePass);
        if(Preview) renderer.EnqueuePass(opaqueMrtPreviewPass);
        renderer.EnqueuePass(opaqueMrtTextureBlitPass);
    }

    class MRTDrawPass : ScriptableRenderPass
    {
        public RTHandle uvRT;
        public RTHandle ddxyRT;
        public RTHandle wNormRT;
        public RTHandle wPosDDXRT;
        public RTHandle wPosDDYRT;
        public RTHandle dummyRT;
        public ScriptableRenderer renderer;
        public System.Action<RTHandle,RTHandle,RTHandle,RTHandle,RTHandle> onCreatedRT;
        public MRTDrawPass()
        {
            renderPassEvent = RenderPassEvent.AfterRenderingOpaques;
        }

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {            
            var desc = renderingData.cameraData.cameraTargetDescriptor;
            desc.colorFormat = RenderTextureFormat.ARGBFloat;
            desc.sRGB = false;
            desc.depthBufferBits = 0;

            RenderingUtils.ReAllocateIfNeeded(ref uvRT, desc, FilterMode.Point, TextureWrapMode.Clamp);
            RenderingUtils.ReAllocateIfNeeded(ref ddxyRT, desc, FilterMode.Point, TextureWrapMode.Clamp);
            RenderingUtils.ReAllocateIfNeeded(ref wNormRT, desc, FilterMode.Point, TextureWrapMode.Clamp);
            RenderingUtils.ReAllocateIfNeeded(ref wPosDDXRT, desc, FilterMode.Point, TextureWrapMode.Clamp);
            RenderingUtils.ReAllocateIfNeeded(ref wPosDDYRT, desc, FilterMode.Point, TextureWrapMode.Clamp);
            RenderingUtils.ReAllocateIfNeeded(ref dummyRT, desc, FilterMode.Point, TextureWrapMode.Clamp);

            onCreatedRT?.Invoke(uvRT,ddxyRT,wNormRT,wPosDDXRT,wPosDDYRT);
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            var cmd = CommandBufferPool.Get("BakeDecalOpaqueMRT");

            cmd.SetRenderTarget(
                new RenderTargetIdentifier[]
                {
                    dummyRT.nameID,
                    uvRT.nameID,
                    ddxyRT.nameID,
                    wNormRT.nameID,
                    wPosDDXRT.nameID,
                    wPosDDYRT.nameID
                },
                renderer.cameraDepthTargetHandle
            );
            cmd.ClearRenderTarget(false, true, Color.clear);
            
            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);

            var drawingSettings = new DrawingSettings(
                new ShaderTagId("PaintEnvironmentBakeMRT"),
                new SortingSettings(renderingData.cameraData.camera)
            );

            drawingSettings.SetShaderPassName(0, new ShaderTagId("PaintEnvironmentBakeMRT"));

            var filteringSettings = new FilteringSettings(
                RenderQueueRange.opaque
            );
            context.DrawRenderers(renderingData.cullResults, ref drawingSettings, ref filteringSettings);
        }
    }
    class MRTPreviewPass : ScriptableRenderPass
    {
        public RTHandle uvRTPreview;
        public ScriptableRenderer renderer;
        public MRTPreviewPass()
        {
            renderPassEvent = RenderPassEvent.AfterRendering;
        }

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if(uvRTPreview == null)return;
            CommandBuffer cmd = CommandBufferPool.Get("MRTOpaquePreview");
            Blitter.BlitCameraTexture(
                cmd,
                uvRTPreview,
                renderer.cameraColorTargetHandle
            );
            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }
    }
    class MRTTextureBlitPass : ScriptableRenderPass
    {
        public RTHandle uvTextureBlitPass;
        public RTHandle ddxyTextureBlitPass;
        public RTHandle wNormTextureBlitPass;
        public RTHandle ddxWPosTextureBlitPass;
        public RTHandle ddyWPosTextureBlitPass;
        public ScriptableRenderer renderer;
        public MRTTextureBlitPass()
        {
            renderPassEvent = RenderPassEvent.AfterRenderingOpaques;
        }

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            var desc = renderingData.cameraData.cameraTargetDescriptor;
            desc.colorFormat = RenderTextureFormat.ARGBFloat;
            desc.depthBufferBits = 0;

            MRTRenderTextureProvider.EnsureAllocated(desc.width, desc.height, desc.colorFormat);
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (uvTextureBlitPass == null) return;

            var cmd = CommandBufferPool.Get("BakeDecalOpaqueTextureBlit");

            cmd.Blit(uvTextureBlitPass, MRTRenderTextureProvider.uvRTCapture);
            cmd.Blit(ddxyTextureBlitPass, MRTRenderTextureProvider.ddxyRTCapture);
            cmd.Blit(wNormTextureBlitPass, MRTRenderTextureProvider.wNormRTCapture);
            cmd.Blit(ddxWPosTextureBlitPass, MRTRenderTextureProvider.wPosDDXRTCampture);
            cmd.Blit(ddyWPosTextureBlitPass, MRTRenderTextureProvider.wPosDDYRTCampture);

            MRTRenderTextureProvider.hasRenderedRT = true;

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }
    }
    public static class MRTRenderTextureProvider
    {
        public static RenderTexture uvRTCapture;
        public static RenderTexture ddxyRTCapture;
        public static RenderTexture wNormRTCapture;
        public static RenderTexture wPosDDXRTCampture;
        public static RenderTexture wPosDDYRTCampture;
        public static bool hasRenderedRT = false;

        public static void EnsureAllocated(int width, int height, RenderTextureFormat format)
        {
            EnsureRT(ref uvRTCapture, width, height, format);
            EnsureRT(ref ddxyRTCapture, width, height, format);
            EnsureRT(ref wNormRTCapture, width, height, format);
            EnsureRT(ref wPosDDXRTCampture, width, height, format);
            EnsureRT(ref wPosDDYRTCampture, width, height, format);
        }

        private static void EnsureRT(ref RenderTexture rt, int width, int height, RenderTextureFormat format)
        {
            if (rt != null &&
            (rt.width != width || rt.height != height || rt.format != format))
            {
                rt.Release();
                Object.DestroyImmediate(rt);
                rt = null;
            }
            if (rt == null)
            {
                rt = new RenderTexture(width, height, 0, format)
                {
                    useMipMap = false,
                    autoGenerateMips = false,
                };
                rt.Create();
            }
        }
    }
}


