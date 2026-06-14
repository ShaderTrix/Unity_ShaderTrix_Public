using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class BakeDecalToTextureMRT : ScriptableRendererFeature
{
    public bool Preview;
    MRTDrawPass2 decalMrtWritePass;
    MRTPreviewPass2 decalMrtPreviewPass;
    MRTTextureBlitPass2 decalMrtTextureBlitPass;
    public override void Create()
    {
        decalMrtWritePass = null;
        decalMrtPreviewPass = null;
        decalMrtTextureBlitPass = null;
    }

    public override void AddRenderPasses(ScriptableRenderer renderer,ref RenderingData renderingData)
    {
        if (renderingData.cameraData.cameraType != CameraType.Game)
            return;

        decalMrtWritePass ??= new MRTDrawPass2(renderer);
        decalMrtPreviewPass ??= new MRTPreviewPass2(renderer);
        decalMrtTextureBlitPass ??= new MRTTextureBlitPass2(renderer);

        if (decalMrtWritePass.onCreatedRT == null)
        {
            decalMrtWritePass.onCreatedRT += (c) =>
            {
                if(Preview) decalMrtPreviewPass.uvRTPreview = c;
                decalMrtTextureBlitPass.uvTextureBlitPass = c;
            };
        }

        renderer.EnqueuePass(decalMrtWritePass);
        if(Preview) renderer.EnqueuePass(decalMrtPreviewPass);
        renderer.EnqueuePass(decalMrtTextureBlitPass);
    }


    class MRTDrawPass2 : ScriptableRenderPass
    {
        public RTHandle uvRT;
        public RTHandle dummyRT;
        ScriptableRenderer renderer;
        public System.Action<RTHandle> onCreatedRT;
        public MRTDrawPass2(ScriptableRenderer renderer)
        {
            this.renderer = renderer;
            renderPassEvent = RenderPassEvent.AfterRenderingOpaques;
        }

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            var desc = renderingData.cameraData.cameraTargetDescriptor;
            desc.colorFormat = RenderTextureFormat.ARGBFloat;
            desc.depthBufferBits = 0;

            RenderingUtils.ReAllocateIfNeeded(ref uvRT, desc, FilterMode.Point, TextureWrapMode.Clamp);
            RenderingUtils.ReAllocateIfNeeded(ref dummyRT, desc, FilterMode.Point, TextureWrapMode.Clamp);

            onCreatedRT?.Invoke(uvRT);
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            var cmd = CommandBufferPool.Get("BakeDecalMRT");

            cmd.SetRenderTarget(
                new RenderTargetIdentifier[]
                {
                    dummyRT.nameID,
                    uvRT.nameID
                },
                renderer.cameraDepthTargetHandle
            );
            cmd.ClearRenderTarget(false, true, Color.clear);
            
            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);

            var drawingSettings = new DrawingSettings(
                new ShaderTagId("PaintDecalBakeMRT"),
                new SortingSettings(renderingData.cameraData.camera)
            );

            // drawingSettings.SetShaderPassNameCount(1);
            drawingSettings.SetShaderPassName(0, new ShaderTagId("PaintDecalBakeMRT"));

            var filteringSettings = new FilteringSettings(
                RenderQueueRange.opaque
            );
            context.DrawRenderers(renderingData.cullResults, ref drawingSettings, ref filteringSettings);
        }
    }
    class MRTPreviewPass2 : ScriptableRenderPass
    {
        public RTHandle uvRTPreview;
        ScriptableRenderer renderer;
        public MRTPreviewPass2(ScriptableRenderer renderer)
        {
            this.renderer = renderer;
            renderPassEvent = RenderPassEvent.AfterRendering;
        }

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if(uvRTPreview == null)return;
            CommandBuffer cmd = CommandBufferPool.Get("MRT Preview");
            Blitter.BlitCameraTexture(
                cmd,
                uvRTPreview,
                renderer.cameraColorTargetHandle
            );
            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }
    }
    class MRTTextureBlitPass2 : ScriptableRenderPass
    {
        public RTHandle uvTextureBlitPass;
        ScriptableRenderer renderer;
        public MRTTextureBlitPass2(ScriptableRenderer renderer)
        {
            renderPassEvent = RenderPassEvent.AfterRendering;
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

            var cmd = CommandBufferPool.Get("BakeDecalTextureBlit");

            cmd.Blit(uvTextureBlitPass, MRTRenderTextureProvider.decalRTCapture);

            MRTRenderTextureProvider.hasRenderedRT = true;

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }
    }
    public static class MRTRenderTextureProvider
    {
        public static RenderTexture decalRTCapture;

        public static bool hasRenderedRT = false;

        public static void EnsureAllocated(int width, int height, RenderTextureFormat format)
        {
            EnsureRT(ref decalRTCapture, width, height, format);
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
                    autoGenerateMips = false
                };
                rt.Create();
            }
        }
    }
}


