namespace HyperTool.Services;

public interface ITrayService : IDisposable
{
    void Initialize(
        Action showAction,
        Action hideAction,
        Action startDefaultVmAction,
        Action stopDefaultVmAction,
        Action connectDefaultVmAction,
        Action createCheckpointAction,
        Action exitAction);
}