using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class PixelizerGunRendererFeatureMRT : ScriptableRendererFeature
{
    public bool _preview;
    public bool _previewWP;
    public Material _previewMat;
    MRTWritePass writePass;
    MRTScreenBlitPass screenBlitPass;
    MRTTextureBlitPass textureBlitPass;
    private uint _blockSize = 64;
    public override void Create()
    {
        writePass = new MRTWritePass();
        var mat = CoreUtils.CreateEngineMaterial(Shader.Find("Hidden/MRTDownSample"));
        screenBlitPass = new MRTScreenBlitPass(_previewMat);
        textureBlitPass = new MRTTextureBlitPass();

        writePass.onCreatedRT += (c, w) =>
        {
            if(_preview)screenBlitPass.colorRT = _previewWP ? w : c;
            textureBlitPass.colorRT = c;
            textureBlitPass.wPosRT = w;
        };
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
         var camData = renderingData.cameraData;
        // Ignore Scene view + non-game cameras
        if (camData.isSceneViewCamera ||
            camData.camera.cameraType != CameraType.Game)
            return;

        if (!camData.camera.CompareTag("MainCamera"))
            return;
        writePass.renderer = renderer;
        if(_preview)screenBlitPass.renderer = renderer;
        textureBlitPass.renderer = renderer;

        renderer.EnqueuePass(writePass);
        if(_preview)renderer.EnqueuePass(screenBlitPass);
        renderer.EnqueuePass(textureBlitPass);
    }

    class MRTWritePass : ScriptableRenderPass
    {
        public ScriptableRenderer renderer;
        public RTHandle colorRT,wPosRT,dummyRT;
        public System.Action<RTHandle,RTHandle> onCreatedRT;
        public MRTWritePass()
        {
            renderPassEvent = RenderPassEvent.AfterRenderingOpaques;
        }

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            var desc = renderingData.cameraData.cameraTargetDescriptor;
            desc.colorFormat = RenderTextureFormat.ARGBFloat;
            desc.depthBufferBits = 0;

            RenderingUtils.ReAllocateIfNeeded(ref colorRT, desc, FilterMode.Bilinear, TextureWrapMode.Clamp);
            RenderingUtils.ReAllocateIfNeeded(ref wPosRT, desc, FilterMode.Bilinear, TextureWrapMode.Clamp);
            RenderingUtils.ReAllocateIfNeeded(ref dummyRT, desc, FilterMode.Bilinear, TextureWrapMode.Clamp);


            onCreatedRT?.Invoke(colorRT,wPosRT);
            // ConfigureTarget(
            //     new RTHandle[] { renderer.cameraColorTargetHandle,// SV_Target0
            //                      colorRT,// SV_Target1
            //                      wPosRT }// SV_Target2
            // );
        }
        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (colorRT == null || wPosRT == null)
                return;

            CommandBuffer cmd = CommandBufferPool.Get("MRT Write Pass");

            // Bind ONLY colorRT as the current color target (no MRT yet).
            // This is done so we can clear it before attaching it in the MRT setup.
            // cmd.SetRenderTarget(colorRT);

            // // Clear the custom RT. This does NOT affect the camera color buffer.
            // cmd.ClearRenderTarget(false, clearColor: true, backgroundColor: Color.clear);

            // cmd.SetRenderTarget(wPosRT);
            // cmd.ClearRenderTarget(false, clearColor: true, backgroundColor: Color.clear);
            // Bind BOTH render targets for MRT:
            //   SV_Target0 → cameraColorTarget
            //   SV_Target1 → colorRT
            //
            // After this, any shader writing to SV_Target0 or SV_Target1 will write into these textures.
            // The depth attachment still uses the camera's depth buffer.
            // cmd.SetRenderTarget(
            //     new RenderTargetIdentifier[]
            //     {
            //         renderer.cameraColorTargetHandle.nameID, // SV_Target0
            //         colorRT.nameID                           // SV_Target1
            //     },
            //     renderer.cameraColorTargetHandle.nameID       // Depth attachment
            // );
            cmd.SetRenderTarget(
                new RenderTargetIdentifier[]
                {
                    dummyRT.nameID, // SV_Target0,using dummy RT so that its not used anywhere, if we use CameracolorTargget it will be blit into screen
                    colorRT.nameID,                        // SV_Target1
                    wPosRT.nameID// SV_Target2
                },
                renderer.cameraColorTargetHandle      // Depth attachment
            );
            cmd.ClearRenderTarget(true, true, Color.clear);
            // Submit all the above state changes to the GPU.
            // This MUST happen before DrawRenderers, otherwise DrawRenderers would draw to the wrong target.
            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);

            // Configure which shader pass (LightMode) to use and how objects will be sorted.
            var drawingSettings = CreateDrawingSettings(
                new ShaderTagId("MRTWrite"),
                ref renderingData,
                SortingCriteria.CommonOpaque
            );
            // Filter which objects are considered for drawing (opaque only here).
            var filteringSettings = new FilteringSettings(RenderQueueRange.opaque);

            // DrawRenderers triggers Unity's full renderer loop:
            // It iterates through all visible objects (from culling), selects those whose shader has a
            // matching LightMode pass, and renders them into the CURRENTLY BOUND render targets.
            //
            // Because we bound an MRT array above, all shader outputs (SV_Target0 / SV_Target1)
            // now write into cameraColor and colorRT simultaneously.
            //this is the actuall command unity uses to draw everything..internally
            context.DrawRenderers(renderingData.cullResults, ref drawingSettings, ref filteringSettings);
        }
    }
    // blits the putput of first writePass onto the screen//debuggings
    class MRTScreenBlitPass : ScriptableRenderPass
    {
        public ScriptableRenderer renderer;
        public RTHandle colorRT;
        private Material mat;
        public MRTScreenBlitPass(Material m)
        {
            renderPassEvent = RenderPassEvent.AfterRendering;
            mat = m;
        }
        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            renderingData.cameraData.requiresDepthTexture = true;
        }
        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (colorRT == null) return;

            CommandBuffer cmd = CommandBufferPool.Get("MRT Blit");
            // blitMaterial.SetInt(Shader.PropertyToID("_BlockSize"), blockSize);
            Blitter.BlitCameraTexture(
                cmd,
                colorRT,
                renderer.cameraColorTargetHandle,
                mat,
                0
            );
            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }
    }
    class MRTTextureBlitPass : ScriptableRenderPass
    {
        public ScriptableRenderer renderer;
        public RTHandle colorRT,wPosRT;
         public MRTTextureBlitPass()
        {
            renderPassEvent = RenderPassEvent.AfterRendering;
        }
        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            var camData = renderingData.cameraData;
            Matrix4x4 proj = camData.GetGPUProjectionMatrix(); 
            Matrix4x4 view = camData.GetViewMatrix();
            Matrix4x4 invVP = (proj * view).inverse;
            // cmd.SetGlobalMatrix("_URP_InvViewProj", invVP);

            var desc = renderingData.cameraData.cameraTargetDescriptor;
            desc.depthBufferBits = 0;
            desc.colorFormat = RenderTextureFormat.ARGBFloat;

            MRTRenderTextureProvider.EnsureAllocated(desc.width, desc.height, desc.colorFormat);
        }
       public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (colorRT == null || wPosRT == null) return;

            var cmd = CommandBufferPool.Get("TextureBlit");

            cmd.Blit(colorRT, MRTRenderTextureProvider.colorCapturedRT);
            cmd.Blit(wPosRT,  MRTRenderTextureProvider.wPosCapturedRT);

            MRTRenderTextureProvider.hasRenderedRT = true;

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }
    }
    public static class MRTRenderTextureProvider
    {
        public static RenderTexture colorCapturedRT;
        public static RenderTexture wPosCapturedRT;

        public static bool hasRenderedRT = false;

        public static void EnsureAllocated(int width, int height, RenderTextureFormat format)
        {
            EnsureRT(ref colorCapturedRT, width, height, format);
            EnsureRT(ref wPosCapturedRT, width, height, format);
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
                    enableRandomWrite = true,
                    useMipMap = false,
                    autoGenerateMips = false
                };
                rt.Create();
            }
        }
    }
}
