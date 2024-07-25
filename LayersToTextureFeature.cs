using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RendererUtils;
using UnityEngine.Rendering.Universal;

public class LayersToTextureFeature : ScriptableRendererFeature
{
    [System.Serializable]
    public class Settings
    {
        public string textureName = "_GlobalLayerTexture";
        public LayerMask layerMask = 0;
        public RenderPassEvent renderPassEvent = RenderPassEvent.AfterRenderingOpaques;
    }

    public Settings settings = new();

    private LayerToTexturePass layerToTexturePass;

    public override void Create()
    {
        layerToTexturePass = new LayerToTexturePass(settings.textureName, settings.layerMask, name)
        {
            renderPassEvent = settings.renderPassEvent
        };
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        renderer.EnqueuePass(layerToTexturePass);
    }

    private class LayerToTexturePass : ScriptableRenderPass
    {
        private readonly string textureName;
        private readonly LayerMask layerMask;
        private RTHandle renderTarget;
        private ProfilingSampler sampler;

        public LayerToTexturePass(string textureName, LayerMask layerMask, string name)
        {
            this.textureName = textureName;
            this.layerMask = layerMask;
            sampler = new ProfilingSampler(name);
        }

        public override void Configure(CommandBuffer cmd, RenderTextureDescriptor cameraTextureDescriptor)
        {
            if (renderTarget != null)
            {
                RTHandles.Release(renderTarget);
            }

            renderTarget = RTHandles.Alloc(cameraTextureDescriptor.width, cameraTextureDescriptor.height, 1, DepthBits.None, cameraTextureDescriptor.graphicsFormat, FilterMode.Bilinear, TextureWrapMode.Clamp, name: textureName);

            ConfigureTarget(renderTarget);
            ConfigureClear(ClearFlag.All, Color.clear);
        }


        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            CommandBuffer cmd = CommandBufferPool.Get();

            using (new ProfilingScope(cmd, sampler))
            {
                // Command buffer shouldn't contain anything, but apparently need to
                // execute so DrawRenderers call is put under profiling scope title correctly
                context.ExecuteCommandBuffer(cmd);
                cmd.Clear();

                FilteringSettings filteringSettings = new(RenderQueueRange.all, layerMask);
                DrawingSettings drawingSettings = CreateDrawingSettings(new ShaderTagId("UniversalForward"), ref renderingData, SortingCriteria.CommonOpaque);

                var rendererListDesc = new RendererListDesc(new ShaderTagId("UniversalForward"), renderingData.cullResults, renderingData.cameraData.camera)
                {
                    sortingCriteria = drawingSettings.sortingSettings.criteria,
                    renderQueueRange = filteringSettings.renderQueueRange,
                    excludeObjectMotionVectors = false,
                    layerMask = layerMask,
                    overrideMaterial = null,
                    overrideMaterialPassIndex = 0
                };

                RendererList rendererList = context.CreateRendererList(rendererListDesc);

                cmd.DrawRendererList(rendererList);
                cmd.SetGlobalTexture(textureName, renderTarget);

                // Execute Command Buffer one last time and release it
                // (Otherwise we get weird recursive list in Frame Debugger)
                context.ExecuteCommandBuffer(cmd);
                cmd.Clear();
                CommandBufferPool.Release(cmd);
            }
        }

        public override void OnCameraCleanup(CommandBuffer cmd)
        {
            RTHandles.Release(renderTarget);
        }
    }
}