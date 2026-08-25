using System;
using UnityEngine;

/// <summary>
/// Compatibility logger required by the recovered UMP package. In its source
/// project this type came from an unrelated UI framework.
/// </summary>
public static class Log
{
    public static Action<object> Debug = UnityEngine.Debug.Log;
    public static Action<object> Error = UnityEngine.Debug.LogError;

    public static void Configure(bool enableDebugLogs)
    {
        Debug = enableDebugLogs ? (Action<object>)UnityEngine.Debug.Log : Ignore;
    }

    private static void Ignore(object value)
    {
    }
}
