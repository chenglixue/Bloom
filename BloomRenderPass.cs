using System;
using System.Collections.Generic;

namespace UnityEngine.Rendering.Universal
{
    class BloomRenderPass : ScriptableRenderPass
    {
        #region Declaration
        private BloomRenderFeature.PassSetting _passSetting;
        private BloomVolume _bloomVolume;
        private ComputeShader _computeShader;

        private static readonly string _cameraColorTexName = "_CameraColorAttachmentA";
        private static readonly int _cameraColorTexID = Shader.PropertyToID(_cameraColorTexName);
        private RenderTargetIdentifier _cameraColorIdentifier;
        private RenderTextureDescriptor _descriptor;

        private Vector2Int _texSize;
        #endregion
        
        #region Prepare
        public BloomRenderPass(BloomRenderFeature.PassSetting passSetting)
        {
            this._passSetting = passSetting;
            renderPassEvent = _passSetting.passEvent;
            if (_passSetting.computeShader == null)
            {
                Debug.LogError("Compute shader is missing!");
            }
            else
            {
                _computeShader = _passSetting.computeShader;
            }

            _cameraColorIdentifier = new RenderTargetIdentifier(_cameraColorTexID);
        }
        public void Setup(BloomVolume bloomVolume)
        {
            _bloomVolume = bloomVolume;
        }
        
        private Vector4 GetTexSizeParams(Vector2Int size)
        {
            return new Vector4(size.x, size.y, 1.0f / size.x, 1.0f / size.y);
        }
        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            _descriptor = renderingData.cameraData.cameraTargetDescriptor;

            _texSize = new Vector2Int(_descriptor.width, _descriptor.height);
            _descriptor.enableRandomWrite = true;
            _descriptor.depthBufferBits = 0;
            _descriptor.msaaSamples = 1;
            _descriptor.colorFormat = RenderTextureFormat.DefaultHDR;

            _cameraColorIdentifier = renderingData.cameraData.renderer.cameraColorTarget;
        }
        
        public override void OnCameraCleanup(CommandBuffer cmd)
        {
            if(cmd == null) throw new ArgumentNullException("cmd");
        }
        
        #endregion

        #region Core
        private void DoBloomDownSample(CommandBuffer cmd, RenderTargetIdentifier sourceID,
            RenderTargetIdentifier targetID, Vector2Int sourceTexSize, Vector2Int targetTexSize, bool isFirstDownSample)
        {
            if (_computeShader == null)
            {
                Debug.LogError("compute shader is missing!");
                return;
            }

            string kernelName = isFirstDownSample ? "BloomWeightedDownSample" : "BloomDownSample";
            int kernelIndex = _computeShader.FindKernel(kernelName);
            _computeShader.GetKernelThreadGroupSizes(kernelIndex, out uint x, out uint y, out uint z);
            
            cmd.SetComputeTextureParam(_computeShader, kernelIndex, "_SourceTex", sourceID);
            cmd.SetComputeTextureParam(_computeShader, kernelIndex, "_RW_TargetTex", targetID);
            cmd.SetComputeVectorParam(_computeShader,  "_SourceTexSize", GetTexSizeParams(sourceTexSize));
            cmd.SetComputeVectorParam(_computeShader,  "_TargetSize", GetTexSizeParams(targetTexSize));
            cmd.SetComputeFloatParam(_computeShader, "_LuminanceThreshold", _bloomVolume.luminanceThreshold.value);
            cmd.DispatchCompute(_computeShader, kernelIndex, Mathf.CeilToInt((float)targetTexSize.x / x), Mathf.CeilToInt((float)targetTexSize.y / y), 1);
        }

        private void DoBloomAdditiveUpSample(CommandBuffer cmd, RenderTargetIdentifier sourceID,
            RenderTargetIdentifier targetID, Vector2Int sourceTexSize, Vector2Int targetTexSize)
        {
            if (_computeShader == null)
            {
                Debug.LogError("compute shader is missing!");
                return;
            }

            const string kernelName = "BloomAdditiveUpSample";
            int kernelIndex = _computeShader.FindKernel(kernelName);
            
            _computeShader.GetKernelThreadGroupSizes(kernelIndex, out uint x, out uint y, out uint z);
            cmd.SetComputeTextureParam(_computeShader, kernelIndex, "_SourceTex", sourceID);
            cmd.SetComputeTextureParam(_computeShader, kernelIndex, "_RW_TargetTex", targetID);
            cmd.SetComputeVectorParam(_computeShader,  "_SourceTexSize", GetTexSizeParams(sourceTexSize));
            cmd.SetComputeVectorParam(_computeShader,  "_TargetSize", GetTexSizeParams(targetTexSize));
            cmd.DispatchCompute(_computeShader, kernelIndex, Mathf.CeilToInt((float)targetTexSize.x / x), Mathf.CeilToInt((float)targetTexSize.y / y), 1);
        }

        private void DoBloomBlendSource(CommandBuffer cmd, RenderTargetIdentifier colorID, RenderTargetIdentifier sourceID,
            RenderTargetIdentifier targetID, Vector2Int sourceTexSize, Vector2Int targetTexSize)
        {
            if (_computeShader == null)
            {
                Debug.LogError("compute shader is missing!");
                return;
            }

            const string kernelName = "BloomBlendCameraColor";
            int kernelIndex = _computeShader.FindKernel(kernelName);
            
            _computeShader.GetKernelThreadGroupSizes(kernelIndex, out uint x, out uint y, out uint z);
            cmd.SetComputeTextureParam(_computeShader, kernelIndex, "_SourceTex", sourceID);
            cmd.SetComputeTextureParam(_computeShader, kernelIndex, "_RW_TargetTex", targetID);
            cmd.SetComputeTextureParam(_computeShader, kernelIndex, "_ColorTex", colorID);
            cmd.SetComputeVectorParam(_computeShader,  "_SourceTexSize", GetTexSizeParams(sourceTexSize));
            cmd.SetComputeVectorParam(_computeShader,  "_TargetSize", GetTexSizeParams(targetTexSize));
            cmd.SetComputeFloatParam(_computeShader, "_I_DownSampleCounts", 1f / _bloomVolume.downSampleCounts.value);
            cmd.SetComputeFloatParam(_computeShader, "_BloomIntensity", _bloomVolume.bloomIntensity.value);
            cmd.DispatchCompute(_computeShader, kernelIndex, Mathf.CeilToInt((float)targetTexSize.x / x), Mathf.CeilToInt((float)targetTexSize.y / y), 1);
        }
        
        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
                CommandBuffer cmd = CommandBufferPool.Get();

                using (new ProfilingScope(cmd, new ProfilingSampler(_passSetting.profilerTag)))
                {
                    var tempDesc = _descriptor;
                    
                    List<int> RTIDs = new List<int>();
                    List<Vector2Int> texSizes = new List<Vector2Int>();

                    int bloomID = Shader.PropertyToID("_BloomTex");
                    cmd.GetTemporaryRT(bloomID, tempDesc);
                    RTIDs.Add(bloomID);
                    texSizes.Add(_texSize);

                    int lastDownRT = _cameraColorTexID;
                    var lastDownTexSize = _texSize;
                    int downSampleCounts = _bloomVolume.downSampleCounts.value;
                    for (int i = 0; i < downSampleCounts; ++i)
                    {
                        int currRTID = Shader.PropertyToID("_BloomRT" + i.ToString());
                        var currTexSize = new Vector2Int(((lastDownTexSize.x + 1) / 2), ((lastDownTexSize.y + 1) / 2));
                        tempDesc.width = currTexSize.x;
                        tempDesc.height = currTexSize.y;
                        RTIDs.Add(currRTID);
                        texSizes.Add(currTexSize);
                        cmd.GetTemporaryRT(currRTID, tempDesc);
                        
                        DoBloomDownSample(cmd, lastDownRT, currRTID, lastDownTexSize, currTexSize, i == 0);

                        lastDownRT = currRTID;
                        lastDownTexSize = currTexSize;
                    }
                    
                     for (int i = downSampleCounts; i >= 1; --i)
                     {
                         int sourceRT = RTIDs[i];
                         int targetRT = RTIDs[i - 1];
                         var sourceTexSize = texSizes[i];
                         var targetTexSize = texSizes[i - 1];
                    
                         if (i == 1)
                         {
                             DoBloomBlendSource(cmd, _cameraColorIdentifier, sourceRT, targetRT, sourceTexSize, targetTexSize);
                             cmd.Blit(targetRT, _cameraColorIdentifier);
                             cmd.ReleaseTemporaryRT(targetRT);
                         }
                         else
                         {
                             DoBloomAdditiveUpSample(cmd, sourceRT, targetRT, sourceTexSize, targetTexSize);
                         }
                         
                         cmd.ReleaseTemporaryRT(sourceRT);
                     }
                }
                
                context.ExecuteCommandBuffer(cmd);
                cmd.Clear();
                CommandBufferPool.Release(cmd);
        }
        #endregion
    }   
}