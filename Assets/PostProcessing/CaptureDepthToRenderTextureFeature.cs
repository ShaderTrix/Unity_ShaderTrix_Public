using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class CaptureDepthToRenderTextureFeature : ScriptableRendererFeature
{
    public bool Preview;
    CameraDepthBlitPass depthPass;
    DepthPreviewPass previewPass;
    Material depthMaterial;

    public override void Create()
    {
        depthMaterial = CoreUtils.CreateEngineMaterial("Hidden/SampleDepthShader");

        depthPass = new CameraDepthBlitPass(depthMaterial);
        previewPass = new DepthPreviewPass();
    }

    public override void AddRenderPasses(ScriptableRenderer renderer,ref RenderingData renderingData)
    {
        if (renderingData.cameraData.cameraType != CameraType.Game)
            return;

        renderingData.cameraData.requiresDepthTexture = true;

        depthPass.renderer = renderer;
        previewPass.renderer = renderer;

        renderer.EnqueuePass(depthPass);

        if (Preview)
            renderer.EnqueuePass(previewPass);
    }

    class CameraDepthBlitPass : ScriptableRenderPass
    {
        public ScriptableRenderer renderer;
        Material depthMat;
        public CameraDepthBlitPass(Material mat)
        {
            renderPassEvent = RenderPassEvent.AfterRenderingOpaques;
            depthMat = mat;
        }

        public override void OnCameraSetup(CommandBuffer cmd,ref RenderingData renderingData)
        {
            var desc = renderingData.cameraData.cameraTargetDescriptor;
            desc.depthBufferBits = 0;

            MRTRenderTextureProvider.EnsureAllocated(desc.width,desc.height);
        }

        public override void Execute(ScriptableRenderContext context,ref RenderingData renderingData)
        {
            var cmd = CommandBufferPool.Get("CopyCameraDepth");

            cmd.SetRenderTarget(renderer.cameraColorTargetHandle);
            cmd.Blit(
                renderer.cameraColorTargetHandle,
                MRTRenderTextureProvider.depthCapture,
                depthMat
            );


            MRTRenderTextureProvider.hasRenderedRT = true;

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }
    }

    class DepthPreviewPass : ScriptableRenderPass
    {
        public ScriptableRenderer renderer;

        public DepthPreviewPass()
        {
            renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing;
        }

        public override void Execute(ScriptableRenderContext context,ref RenderingData renderingData)
        {
            if (!MRTRenderTextureProvider.hasRenderedRT || MRTRenderTextureProvider.depthCapture == null)
                return;

            var cmd = CommandBufferPool.Get("PreviewDepth");
            
            cmd.Blit(MRTRenderTextureProvider.depthCapture, renderer.cameraColorTargetHandle);

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }
    }

    public static class MRTRenderTextureProvider
    {
        public static RenderTexture depthCapture;
        public static bool hasRenderedRT;

        public static void EnsureAllocated(int width, int height)
        {
            if (depthCapture != null && (depthCapture.width != width || depthCapture.height != height))
            {
                depthCapture.Release();
                Object.DestroyImmediate(depthCapture);
                depthCapture = null;
            }

            if (depthCapture == null)
            {
                depthCapture = new RenderTexture(width,height,0,RenderTextureFormat.RFloat)
                {
                    useMipMap = false,
                    autoGenerateMips = false,
                    enableRandomWrite = true
                };
                depthCapture.Create();
            }
        }
    }
}
