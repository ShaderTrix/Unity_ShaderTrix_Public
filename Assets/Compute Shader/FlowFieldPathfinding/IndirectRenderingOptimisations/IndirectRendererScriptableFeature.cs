using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class IndirectRendererScriptableFeature : ScriptableRendererFeature
{
    [System.Serializable]
    public class Settings
    {
        public ComputeShader _visibilityShader;
        public int MAXIMUM_AGENTS_COUNT = 20000;
        public int MAXIMUM_BUFFER_SIZE = 512; // safer default for Macs
        public int MAX_LODS = 8;
    }

    [SerializeField] private Settings settings = new Settings();

    class HIZRenderPass : ScriptableRenderPass
    {
        readonly Settings _settings;
        public Material hiZMaterial;
        RenderTexture hiZRT;
        int lodCount;
        RenderTargetIdentifier colorTarget, depthTarget;
        bool validForCamera = false;

        public HIZRenderPass(Settings settings)
        {
            _settings = settings;
        }

        RenderTexture CreateRT(int size)
        {
            var desc = new RenderTextureDescriptor(size, size, RenderTextureFormat.RGHalf, 0)
            {
                useMipMap = true,
                autoGenerateMips = false,
                msaaSamples = 1,
                sRGB = false
            };
            var rt = new RenderTexture(desc)
            {
                filterMode = FilterMode.Point,
                hideFlags = HideFlags.HideAndDontSave,
                wrapMode = TextureWrapMode.Clamp
            };
            rt.Create();
            return rt;
        }

        public void ReleaseHiZ()
        {
            if (hiZRT == null)
                return;

            hiZRT.Release();

        #if UNITY_EDITOR
            if (!Application.isPlaying)
                DestroyImmediate(hiZRT);
            else
        #endif
                Destroy(hiZRT);

            hiZRT = null;
        }


        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            var renderer = renderingData.cameraData.renderer;
            colorTarget = renderer.cameraColorTargetHandle;
            depthTarget = renderer.cameraDepthTargetHandle;

            var cam = renderingData.cameraData.camera;

            // Skip scene view and preview cameras
            if (renderingData.cameraData.isSceneViewCamera || renderingData.cameraData.isPreviewCamera)
            {
                validForCamera = false;
                return;
            }

            validForCamera = true;

            int size = Mathf.Min(Mathf.NextPowerOfTwo(Mathf.Max(cam.pixelWidth, cam.pixelHeight)), _settings.MAXIMUM_BUFFER_SIZE);
            lodCount = Mathf.Min((int)Mathf.Floor(Mathf.Log(size, 2)), _settings.MAX_LODS);
            lodCount = Mathf.Max(1, lodCount);

            if (hiZRT == null || hiZRT.width != size)
            {
                ReleaseHiZ();
                hiZRT = CreateRT(size);
            }
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (!validForCamera || hiZRT == null || hiZMaterial == null)
                return;

            CommandBuffer cmd = CommandBufferPool.Get("HiZ Command Buffer");

            // initial blit from depth buffer into hiZRT mip 0
            cmd.Blit(depthTarget, hiZRT, hiZMaterial, 0);

            int currentSize = hiZRT.width;
            var tempIDs = new List<int>(lodCount);

            // build mip chain using temporaries then copy into hiZRT mips
            for (int i = 0; i < lodCount; ++i)
            {
                int mipSize = Mathf.Max(currentSize >> 1, 1);
                currentSize = mipSize;
                int tmpID = Shader.PropertyToID($"_HiZ_tmp_{i}");
                tempIDs.Add(tmpID);

                cmd.GetTemporaryRT(tmpID, mipSize, mipSize, 0, FilterMode.Point, RenderTextureFormat.RGHalf, RenderTextureReadWrite.Linear);

                if (i == 0)
                {
                    cmd.Blit(hiZRT, tmpID, hiZMaterial, 1); // downsample from mip0
                }
                else
                {
                    cmd.Blit(tempIDs[i - 1], tmpID, hiZMaterial, 1); // chain downsample
                }

                // copy temporary into hiZRT mip (mip levels start at 1)
                cmd.CopyTexture(tmpID, 0, 0, hiZRT, 0, i + 1);
            }

            // release temporaries deterministically
            for (int i = 0; i < tempIDs.Count; ++i)
            {
                cmd.ReleaseTemporaryRT(tempIDs[i]);
            }
            // Only set global if different to avoid pointless driver churn
            // if (Shader.GetGlobalTexture("_HiZDepthTexture") != hiZRT)
            //     Shader.SetGlobalTexture("_HiZDepthTexture", hiZRT);
            // bind HiZ globally and to compute if available
            if (_settings._visibilityShader != null)
            {
                int kernelID = _settings._visibilityShader.FindKernel("CSMain");
                var cam = renderingData.cameraData.camera;
                _settings._visibilityShader.SetTexture(kernelID, "_HiZDepthTexture", hiZRT);
                Matrix4x4 vp = GL.GetGPUProjectionMatrix(cam.projectionMatrix, true) * cam.worldToCameraMatrix;
                _settings._visibilityShader.SetMatrix("_VPMatrix", vp);
                _settings._visibilityShader.SetVector("_CameraPos", cam.transform.position);
                _settings._visibilityShader.SetInt("_HiZTextureSize", hiZRT != null ? hiZRT.width : 0);
                _settings._visibilityShader.SetInt("_HiZMipCount", lodCount);
                _settings._visibilityShader.SetInt("_AgentCount",_settings.MAXIMUM_AGENTS_COUNT); // make sure you actually set this
            }
        
            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        public override void OnCameraCleanup(CommandBuffer cmd)
        {
            // nothing persistent to cleanup per-camera here.
            // temporaries were released inside Execute via command buffer.
        }

        public void DisposeResources()
        {
            ReleaseHiZ();
            if (hiZMaterial != null)
            {
                CoreUtils.Destroy(hiZMaterial);
                hiZMaterial = null;
            }
        }
    }

    HIZRenderPass m_ScriptablePass;

    public override void Create()
    {
        m_ScriptablePass = new HIZRenderPass(settings)
        {
            renderPassEvent = RenderPassEvent.AfterRenderingSkybox,
            hiZMaterial = CoreUtils.CreateEngineMaterial("Hidden/HizDepthShader")
        };
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (m_ScriptablePass != null)
            renderer.EnqueuePass(m_ScriptablePass);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && m_ScriptablePass != null)
        {
            m_ScriptablePass.DisposeResources();
            m_ScriptablePass = null;
        }
        base.Dispose(disposing);
    }
}
