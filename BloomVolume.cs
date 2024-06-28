using System;

namespace UnityEngine.Rendering.Universal
{
    [Serializable, VolumeComponentMenuForRenderPipeline("Post-processing/Elysia Bloom", typeof(UniversalRenderPipeline))]
    public class BloomVolume : VolumeComponent, IPostProcessComponent
    {
        public BoolParameter enable = new BoolParameter(true);
        
        public ClampedFloatParameter luminanceThreshold = new ClampedFloatParameter(0.9f, 0f, 5f);
        
        public ClampedFloatParameter bloomIntensity = new ClampedFloatParameter(1f, 0f, 1f);
        
        public ClampedIntParameter downSampleCounts = new ClampedIntParameter(5, 3, 10);
        
        
        
        
        
        
        public bool IsTileCompatible() => false;
        public bool IsActive() => enable == true; 
    }
}