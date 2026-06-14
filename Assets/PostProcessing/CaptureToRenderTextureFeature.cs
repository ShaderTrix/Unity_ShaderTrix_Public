using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class CaptureToRenderTextureFeature : ScriptableRendererFeature
{
    RTHandle _rtHandle;
    CapturePass _pass;
    public string _howToUse01 = "RenderTexture tex = CameraCaptureProvider.AsRenderTexture;";
    public string _howToUse02 = "RTHandle h = CameraCaptureProvider.CapturedRT;";
    public override void Create()
    {
        _pass = new CapturePass();
        AllocateRT();
    }

    void AllocateRT()
    {
        if (_rtHandle != null)
            _rtHandle.Release();

        _rtHandle = RTHandles.Alloc(
            Screen.width,
            Screen.height,
            colorFormat: GraphicsFormat.R8G8B8A8_UNorm,
            name: "CapturedCameraRT"
        );

        CameraRenderTextureProvider.CapturedRT = _rtHandle;
        _pass.SetTarget(_rtHandle);
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (renderingData.cameraData.cameraType != CameraType.Game)
            return;

        if (_rtHandle.rt.width != Screen.width || _rtHandle.rt.height != Screen.height)
            AllocateRT();

        renderer.EnqueuePass(_pass);
    }

    protected override void Dispose(bool disposing)
    {
        _rtHandle?.Release();
        CameraRenderTextureProvider.CapturedRT = null;
    }

    class CapturePass : ScriptableRenderPass
    {
        RTHandle _source;
        RTHandle _target;

        public CapturePass()
        {
            renderPassEvent = RenderPassEvent.AfterRendering;
        }

        public void SetTarget(RTHandle target)
        {
            _target = target;
        }

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            _source = renderingData.cameraData.renderer.cameraColorTargetHandle;
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (_target == null)
                return;

            var cmd = CommandBufferPool.Get("CaptureCameraToRT");
            Blitter.BlitCameraTexture(cmd, _source, _target);
            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }
    }
}
public static class CameraRenderTextureProvider
{
    public static RTHandle CapturedRT { get; internal set; }

    public static RenderTexture AsRenderTexture =>
        CapturedRT != null ? CapturedRT.rt : null;
}
