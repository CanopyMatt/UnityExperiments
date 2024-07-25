using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RendererUtils;
using UnityEngine.Rendering.Universal;

public class LayersToTextureFeature : ScriptableRendererFeature
{
    public string textureName = "_LayersToTexture";
    public LayerMask layerMask = 0;
    public RenderPassEvent _event;
    private RenderLayersToTexturePass layerToTexturePass;

    public override void Create()
    {
        layerToTexturePass = new RenderLayersToTexturePass(textureName, layerMask, name)
        {
            renderPassEvent = _event
        };
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        renderer.EnqueuePass(layerToTexturePass);
    }

    protected override void Dispose(bool disposing)
    {
        layerToTexturePass.Dispose();
    }

    private class RenderLayersToTexturePass : ScriptableRenderPass
    {
        private readonly string textureName;
        private readonly LayerMask layerMask;
        private RTHandle renderTarget;
        protected ProfilingSampler sampler;

        public RenderLayersToTexturePass(string textureName, LayerMask layerMask, string name)
        {
            this.textureName = textureName;
            this.layerMask = layerMask;
            sampler = new ProfilingSampler(name);
        }

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            RenderTextureDescriptor colorDescriptor = renderingData.cameraData.cameraTargetDescriptor;
            colorDescriptor.depthBufferBits = 0;
            RenderingUtils.ReAllocateIfNeeded(ref renderTarget, colorDescriptor, name: textureName);
            ConfigureTarget(renderTarget, renderingData.cameraData.renderer.cameraDepthTargetHandle);
            ConfigureClear(ClearFlag.Color, Color.clear);
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            CommandBuffer cmd = CommandBufferPool.Get();

            using (new ProfilingScope(cmd, sampler))
            {
                // Need to command buffer to execute so drawing renderers is put under profiling scope title correctly
                context.ExecuteCommandBuffer(cmd);
                cmd.Clear();

                // Create the render list desc to use on DrawRendererList
                RendererListDesc rendererListDesc = new(new ShaderTagId("UniversalForward"), renderingData.cullResults, renderingData.cameraData.camera);
                rendererListDesc.layerMask = layerMask;

                // Create and draw the render list
                RendererList rendererList = context.CreateRendererList(rendererListDesc);
                cmd.DrawRendererList(rendererList);

                // Pass our custom target to shaders as a Global Texture reference
                cmd.SetGlobalTexture(textureName, renderTarget);

                // Execute Command Buffer again and release it
                // Otherwise we get recursive list in Frame Debugger
                context.ExecuteCommandBuffer(cmd);
                cmd.Clear();
                CommandBufferPool.Release(cmd);
            }
        }

        public void Dispose()
        {
            if (renderTarget != null)
            {
                RTHandles.Release(renderTarget);
            }
        }
    }
}
