using System;

namespace UnityEngine.Rendering.Universal
{
    public class BloomRenderFeature : ScriptableRendererFeature
    {
        [System.Serializable]
        public class PassSetting
        {
            public string profilerTag = "Elysia Bloom Pass";
            
            public RenderPassEvent passEvent = RenderPassEvent.BeforeRenderingPostProcessing;
        
            public ComputeShader computeShader;
        }
        
        public PassSetting passSetting = new PassSetting();
        private BloomRenderPass _bloomPass;
        
        public override void Create()
        {
            _bloomPass = new BloomRenderPass(passSetting);
        }
    
        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            BloomVolume bloomVolume = VolumeManager.instance.stack.GetComponent<BloomVolume>();

            if (bloomVolume != null && bloomVolume.IsActive())
            {
                _bloomPass.Setup(bloomVolume);
                renderer.EnqueuePass(_bloomPass);
            }
        }
    }
}

