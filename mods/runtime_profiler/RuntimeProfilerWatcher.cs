using Il2CppInterop.Runtime.Injection;
using UnityEngine;

namespace SneakOut.RuntimeProfiler;

public sealed class RuntimeProfilerWatcher : MonoBehaviour
{
    public RuntimeProfilerWatcher(IntPtr pointer) : base(pointer)
    {
    }

    public RuntimeProfilerWatcher() : base(ClassInjector.DerivedConstructorPointer<RuntimeProfilerWatcher>())
    {
        ClassInjector.DerivedConstructorBody(this);
    }

    private void Update()
    {
        RuntimeProfilerRuntime.ObserveFrame();
    }

    private void OnApplicationFocus(bool focused)
    {
        RuntimeProfilerRuntime.ObserveApplicationFocus(focused);
    }

    private void OnApplicationPause(bool paused)
    {
        RuntimeProfilerRuntime.ObserveApplicationPause(paused);
    }
}
