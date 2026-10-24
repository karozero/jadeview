using System.Numerics;
using Raylib_cs;

namespace JadeView;

public sealed partial class SceneRenderer
{
    private readonly GpuUniformValueCache<int> integerUniformValues = new();
    private readonly GpuUniformValueCache<float> floatUniformValues = new();
    private readonly GpuUniformValueCache<Vector2> vector2UniformValues = new();
    private readonly GpuUniformValueCache<Vector4> vector4UniformValues = new();
    private readonly GpuUniformValueCache<Matrix4x4> matrixUniformValues = new();
    private uint uniformValueShaderId;
    internal bool CacheShaderUniformValues { get; set; } = true;
    public int StatShaderUniformRequests { get; private set; }
    public int StatShaderUniformUploads { get; private set; }

    private bool NeedsUniformUpload<T>(GpuUniformValueCache<T> cache, int location, T value) where T : unmanaged
    {
        if (uniformValueShaderId != _jadeShader.Id) ResetJadeUniformCache();
        StatShaderUniformRequests++;
        bool changed = cache.Changed(location, value);
        if (location < 0 || CacheShaderUniformValues && !changed) return false;
        StatShaderUniformUploads++;
        return true;
    }

    private unsafe void JadeUniform(string name, int value)
    {
        int location = _jadeUniforms[name];
        if (NeedsUniformUpload(integerUniformValues, location, value))
            Raylib.SetShaderValue(_jadeShader, location, &value, ShaderUniformDataType.Int);
    }

    private unsafe void JadeUniform(string name, float value)
    {
        int location = _jadeUniforms[name];
        if (NeedsUniformUpload(floatUniformValues, location, value))
            Raylib.SetShaderValue(_jadeShader, location, &value, ShaderUniformDataType.Float);
    }

    private unsafe void JadeUniform(string name, Vector2 value)
    {
        int location = _jadeUniforms[name];
        if (NeedsUniformUpload(vector2UniformValues, location, value))
            Raylib.SetShaderValue(_jadeShader, location, &value, ShaderUniformDataType.Vec2);
    }

    private unsafe void JadeUniform(string name, Vector4 value)
    {
        int location = _jadeUniforms[name];
        if (NeedsUniformUpload(vector4UniformValues, location, value))
            Raylib.SetShaderValue(_jadeShader, location, &value, ShaderUniformDataType.Vec4);
    }

    private void JadeMatrixUniform(string name, Matrix4x4 value)
    {
        int location = _jadeUniforms[name];
        if (NeedsUniformUpload(matrixUniformValues, location, value))
            Raylib.SetShaderValueMatrix(_jadeShader, location, value);
    }

    private void ResetJadeUniformCache()
    {
        uniformValueShaderId = _jadeShader.Id;
        integerUniformValues.Clear();
        floatUniformValues.Clear();
        vector2UniformValues.Clear();
        vector4UniformValues.Clear();
        matrixUniformValues.Clear();
    }
}
