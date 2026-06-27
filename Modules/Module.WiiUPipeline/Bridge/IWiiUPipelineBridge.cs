using Module.Core;
using Module.Core.Pipeline;

namespace Module.WiiUPipeline.Bridge;

/// <summary>Bridge for Wii U pipeline status events. Mirrors <c>IPs3PipelineBridge</c>.</summary>
public interface IWiiUPipelineBridge : IModuleBridge<PipelineStatusEvent>;
