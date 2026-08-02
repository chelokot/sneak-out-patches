using Il2CppInterop.Runtime.Injection;
using UnityEngine;

namespace SneakOut.PerformanceOptimizer;

public sealed class PerformanceFrameWatcher : MonoBehaviour
{
    public PerformanceFrameWatcher(IntPtr pointer) : base(pointer)
    {
    }

    public PerformanceFrameWatcher() : base(ClassInjector.DerivedConstructorPointer<PerformanceFrameWatcher>())
    {
        ClassInjector.DerivedConstructorBody(this);
    }

    private void Update()
    {
        PerformanceOptimizerRuntime.ObserveFrame();
    }
}
