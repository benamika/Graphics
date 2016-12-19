using UnityEngine;
using UnityEngine.Experimental.VFX;
using System;
using System.Collections;
using System.Collections.Generic;

namespace UnityEditor.Experimental
{
    public class VFXPointOutputShaderGeneratorModule : VFXOutputShaderGeneratorModule
    {
        public override bool UpdateAttributes(Dictionary<VFXAttribute, VFXAttribute.Usage> attribs, ref VFXBlockDesc.Flag flags)
        {
            if (!UpdateFlag(attribs, CommonAttrib.Position, VFXContextDesc.Type.kTypeOutput))
            {
                Debug.LogError("Position attribute is needed for point output context");
                return false;
            }

            UpdateFlag(attribs, CommonAttrib.Color, VFXContextDesc.Type.kTypeOutput);
            UpdateFlag(attribs, CommonAttrib.Alpha, VFXContextDesc.Type.kTypeOutput);
            return true;
        }

        public override void WritePostBlock(ShaderSourceBuilder builder, ShaderMetaData data)
        {
            builder.Write("float3 worldPos = ");
            builder.WriteAttrib(CommonAttrib.Position, data, ShaderMetaData.Pass.kOutput);
            builder.WriteLine(";");
            builder.WriteLineFormat("o.pos = mul({0}, float4(worldPos,1.0f));", (data.system.WorldSpace ? "UNITY_MATRIX_VP" : "UNITY_MATRIX_MVP"));
        }

        public override int GetOutputType()
        {
            return 0;
        }
    }

    public class VFXBillboardOutputShaderGeneratorModule : VFXOutputShaderGeneratorModule
    {
        public enum OrientMode
        {
            kFaceCamera,
            kVelocity,
            kRotateAxis,
            kFixed,
            kCustom,
        }

        public const int TextureIndex = 0;
        public const int FlipbookDimIndex = 1;
        public const int MorphTextureIndex = 2;
        public const int MorphIntensityIndex = 3;

        public const int FirstLockedAxisIndex = 2;
        public const int SecondLockedAxisIndex = 3;

        public VFXBillboardOutputShaderGeneratorModule(VFXPropertySlot[] slots, OrientMode orientmode)
        {
            for (int i = 0; i < Math.Min(slots.Length, 4); ++i)
                m_Values[i] = slots[i].ValueRef.Reduce() as VFXValue; // TODO Refactor
            m_OrientMode = orientmode;
        }

        public override int[] GetSingleIndexBuffer(ShaderMetaData data) { return new int[0]; } // tmp

        public override int GetOutputType()
        {
            return 1;
        }

        private bool CanHaveMotionVectors()
        {
            return m_OrientMode != OrientMode.kRotateAxis && m_OrientMode != OrientMode.kFixed;
        }

        public override bool UpdateAttributes(Dictionary<VFXAttribute, VFXAttribute.Usage> attribs, ref VFXBlockDesc.Flag flags)
        {
            if (!UpdateFlag(attribs, CommonAttrib.Position, VFXContextDesc.Type.kTypeOutput))
            {
                Debug.LogError("Position attribute is needed for billboard output context");
                return false;
            }

            UpdateFlag(attribs, CommonAttrib.Color, VFXContextDesc.Type.kTypeOutput);
            UpdateFlag(attribs, CommonAttrib.Alpha, VFXContextDesc.Type.kTypeOutput);
            m_HasSize = UpdateFlag(attribs, CommonAttrib.Size, VFXContextDesc.Type.kTypeOutput);
            m_HasAngle = UpdateFlag(attribs, CommonAttrib.Angle, VFXContextDesc.Type.kTypeOutput);
            m_HasPivot = UpdateFlag(attribs, CommonAttrib.Pivot, VFXContextDesc.Type.kTypeOutput);

            if (m_Values[TextureIndex] != null)
            {
                m_HasTexture = true;
                if (m_HasFlipBook = m_Values[FlipbookDimIndex] != null && UpdateFlag(attribs, CommonAttrib.TexIndex, VFXContextDesc.Type.kTypeOutput))
                    m_HasMotionVectors = CanHaveMotionVectors() && m_Values[MorphTextureIndex] != null && m_Values[MorphIntensityIndex] != null && m_Values[MorphTextureIndex].Get<Texture2D>() != null;
            }

            if (m_OrientMode == OrientMode.kVelocity)
            {
                if(!UpdateFlag(attribs, CommonAttrib.Velocity, VFXContextDesc.Type.kTypeOutput))
                    m_OrientMode = OrientMode.kFaceCamera;
            }

            m_HasFront = UpdateFlag(attribs, CommonAttrib.Front, VFXContextDesc.Type.kTypeOutput);
            m_HasSide = UpdateFlag(attribs, CommonAttrib.Side, VFXContextDesc.Type.kTypeOutput);
            m_HasUp = UpdateFlag(attribs, CommonAttrib.Up, VFXContextDesc.Type.kTypeOutput);

            return true;
        }

        public override void UpdateUniforms(HashSet<VFXExpression> uniforms, ref VFXBlockDesc.Flag flags)
        {
            if (m_HasTexture)
            {
                uniforms.Add(m_Values[TextureIndex]);
                if (m_HasFlipBook)
                {
                    uniforms.Add(m_Values[FlipbookDimIndex]);
                    if (m_HasMotionVectors)
                    {
                        uniforms.Add(m_Values[MorphTextureIndex]);
                        uniforms.Add(m_Values[MorphIntensityIndex]);
                    }
                }
            }

            switch(m_OrientMode)
            {
                case OrientMode.kRotateAxis:
                    uniforms.Add(m_Values[FirstLockedAxisIndex]);
                    break;
                case OrientMode.kFixed:
                    uniforms.Add(m_Values[FirstLockedAxisIndex]);
                    uniforms.Add(m_Values[SecondLockedAxisIndex]);
                    break;
                default: break;
            }
        }

        public override void WriteIndex(ShaderSourceBuilder builder, ShaderMetaData data)
        {
            builder.WriteLine("uint index = (id >> 2) + instanceID * 2048;");
        }

        public override void WriteAdditionalVertexOutput(ShaderSourceBuilder builder, ShaderMetaData data)
        {
            builder.WriteLine("float2 offsets : TEXCOORD0;");
            if (m_HasFlipBook)
                builder.WriteLine("nointerpolation float flipbookIndex : TEXCOORD1;");
        }

        private void WriteRotation(ShaderSourceBuilder builder, ShaderMetaData data)
        {
            builder.WriteLine("float2 sincosA;");
            builder.Write("sincos(radians(");
            builder.WriteAttrib(CommonAttrib.Angle, data, ShaderMetaData.Pass.kOutput);
            builder.Write("), sincosA.x, sincosA.y);");
            builder.WriteLine();
            builder.WriteLine("const float c = sincosA.y;");
            builder.WriteLine("const float s = sincosA.x;");
            builder.WriteLine("const float t = 1.0 - c;");
            builder.WriteLine("const float x = front.x;");
            builder.WriteLine("const float y = front.y;");
            builder.WriteLine("const float z = front.z;");
            builder.WriteLine();
            builder.WriteLine("float3x3 rot = float3x3(t * x * x + c, t * x * y - s * z, t * x * z + s * y,");
            builder.WriteLine("\t\t\t\t\tt * x * y + s * z, t * y * y + c, t * y * z - s * x,");
            builder.WriteLine("\t\t\t\t\tt * x * z - s * y, t * y * z + s * x, t * z * z + c);");
            builder.WriteLine();
        }

        public override void WritePostBlock(ShaderSourceBuilder builder, ShaderMetaData data)
        {
            const bool CLAMP_SIZE = false; // false atm

            if (m_HasSize)
            {
                builder.Write("float2 size = ");
                builder.WriteAttrib(CommonAttrib.Size, data, ShaderMetaData.Pass.kOutput);
                builder.WriteLine(" * 0.5f;");
            }
            else
                builder.WriteLine("float2 size = float2(0.005,0.005);");

            builder.WriteLine("o.offsets.x = 2.0 * float(id & 1) - 1.0;");
            builder.WriteLine("o.offsets.y = 2.0 * float((id & 2) >> 1) - 1.0;");
            builder.WriteLine();

            builder.Write("float3 position = ");
            builder.WriteAttrib(CommonAttrib.Position, data, ShaderMetaData.Pass.kOutput);
            builder.WriteLine(";");
            builder.WriteLine();

            if (CLAMP_SIZE)
            {
                builder.WriteLine("// Clamp size so that billboards are never less than one pixel size"); 
                //builder.WriteLine("const float PIXEL_SIZE = (max(_ScreenParams.z,_ScreenParams.w) - 1.0f); // This should be a uniform depending on fov and viewport dimension");
                builder.WriteLine("float4 clipPos = mul(UNITY_MATRIX_MVP,float4(position,1.0f));");
                builder.WriteLine("float currentSize = (min(size.x,size.y) * min(UNITY_MATRIX_P[0][0] * _ScreenParams.x,-UNITY_MATRIX_P[1][1] * _ScreenParams.y)) / clipPos.w;");
                builder.WriteLine("size /= saturate(currentSize);");
/*
                builder.WriteLine("float2 screenPos = min(size.x,size.y) * _ScreenParams.xy * clipPos.xy / clipPos.w");
                builder.WriteLine("minSize = 0.5 * min(screenPos.x,screenPos.y);");
                builder.WriteLine("if (minSize < 1.0f) size /= minSize;");*/
               // if (data.system.WorldSpace)
               //     builder.WriteLine("float minSize = 2.0f * mul(float4(position,1.0f),UNITY_MATRIX_VP).w / (max(_ScreenParams.z,_ScreenParams.w) - 1.0f); // w * pixel size");
               // else
               //     builder.WriteLine("float minSize = 2.0f * mul(float4(position,1.0f),UNITY_MATRIX_MVP).w / (max(_ScreenParams.z,_ScreenParams.w) - 1.0f); // w * pixel size");
               // builder.WriteLine("size = max(size,float2(minSize,minSize));");
                builder.WriteLine();
            }

            if (m_HasPivot)
            {
                builder.Write("float2 posOffsets = o.offsets.xy - ");
                builder.WriteAttrib(CommonAttrib.Pivot, data, ShaderMetaData.Pass.kOutput);
                builder.WriteLine(".xy;");
                builder.WriteLine();
            }
            else
            {
                builder.WriteLine("float2 posOffsets = o.offsets.xy;");
            }

            if (data.system.WorldSpace)
                builder.WriteLine("float3 cameraPos = _WorldSpaceCameraPos.xyz;");
            else
                builder.WriteLine("float3 cameraPos = mul(unity_WorldToObject,float4(_WorldSpaceCameraPos.xyz,1.0)).xyz; // TODO Put that in a uniform!"); 

            switch (m_OrientMode)
            {
                case OrientMode.kVelocity:

                    builder.WriteLine("float3 front = cameraPos - position;");
                    builder.Write("float3 up = normalize(");
                    builder.WriteAttrib(CommonAttrib.Velocity, data, ShaderMetaData.Pass.kOutput);
                    builder.WriteLine(");");
                    builder.WriteLine("float3 side = normalize(cross(front,up));");

                    if (m_HasAngle)
                        builder.WriteLine("front = cross(up,side);");

                    break;

                case OrientMode.kFaceCamera:

                    string toViewSpaceMatrixIT = data.system.WorldSpace ? "unity_WorldToCamera" : "UNITY_MATRIX_IT_MV";

                    if (m_HasAngle || m_HasPivot)
                    {
                        if (data.system.WorldSpace)
                            builder.WriteLine("float3 front = unity_WorldToCamera[2].xyz;");
                        else
                            builder.WriteLine("float3 front = -UNITY_MATRIX_MV[2].xyz;");
                    }

                    builder.WriteLineFormat("float3 side = {0}[0].xyz;", toViewSpaceMatrixIT);
                    builder.WriteLineFormat("float3 up = {0}[1].xyz;", toViewSpaceMatrixIT);

                    break;

                case OrientMode.kRotateAxis:

                    builder.WriteLine("float3 front = cameraPos - position;");
                    builder.Write("float3 up = normalize(");
                    builder.Write(data.paramToName[(int)ShaderMetaData.Pass.kOutput][m_Values[FirstLockedAxisIndex]]);
                    builder.WriteLine(");");
                    builder.WriteLine("float3 side = normalize(cross(front,up));");

                    if (m_HasAngle)
                        builder.WriteLine("front = cross(up,side);");
                    break;

                case OrientMode.kFixed:

                    builder.Write("float3 front = ");
                    builder.Write(data.paramToName[(int)ShaderMetaData.Pass.kOutput][m_Values[SecondLockedAxisIndex]]);
                    builder.Write(";");
                    builder.Write("float3 up = normalize(");
                    builder.Write(data.paramToName[(int)ShaderMetaData.Pass.kOutput][m_Values[FirstLockedAxisIndex]]);
                    builder.WriteLine(");");
                    builder.WriteLine("float3 side = normalize(cross(front,up));");

                    if (m_HasAngle)
                        builder.WriteLine("front = cross(up,side);");
                    break;

                case OrientMode.kCustom:

                    if (m_HasAngle || m_HasPivot)
                    {          
                        builder.Write("float3 front = ");
                        if (m_HasFront)
                            builder.WriteAttrib(CommonAttrib.Front, data, ShaderMetaData.Pass.kOutput);
                        else
                            builder.Write("float3(0.0f,0.0f,1.0f)");
                        builder.WriteLine(";");
                    }
                    
                    builder.Write("float3 side = ");
                    if (m_HasSide)
                        builder.WriteAttrib(CommonAttrib.Side, data, ShaderMetaData.Pass.kOutput);
                    else
                        builder.Write("float3(1.0f,0.0f,0.0f)");
                    builder.WriteLine(";");

                    builder.Write("float3 up = ");
                    if (m_HasUp)
                        builder.WriteAttrib(CommonAttrib.Up, data, ShaderMetaData.Pass.kOutput);
                    else
                        builder.Write("float3(0.0f,1.0f,0.0f)");
                    builder.WriteLine(";");
                    break;

                default: break;
            }

            builder.WriteLine();

            if (m_HasAngle)
            {
                WriteRotation(builder, data);
                builder.WriteLine();
                builder.WriteLine("position += mul(rot,side * posOffsets.x * size.x);");
                builder.WriteLine("position += mul(rot,up * posOffsets.y * size.y);");
            }
            else
            {
                builder.WriteLine("position += side * (posOffsets.x * size.x);");
                builder.WriteLine("position += up * (posOffsets.y * size.y);");
            }

            if (m_HasPivot)
            {
                builder.Write("position -= front * ");
                builder.WriteAttrib(CommonAttrib.Pivot, data, ShaderMetaData.Pass.kOutput);
                builder.WriteLine(".z;");
            }

            if (m_HasTexture)
            {
                builder.WriteLine("o.offsets.xy = o.offsets.xy * 0.5 + 0.5;");
                if (m_HasFlipBook)
                {
                    builder.Write("o.flipbookIndex = ");
                    builder.WriteAttrib(CommonAttrib.TexIndex, data, ShaderMetaData.Pass.kOutput);
                    builder.WriteLine(";");
                }
            }

            if (CLAMP_SIZE)
                builder.WriteLine("local_alpha *= smoothstep(0.5,1,currentSize);");

            builder.WriteLine();
            builder.WriteLineFormat("o.pos = mul ({0}, float4(position,1.0f));", (data.system.WorldSpace ? "UNITY_MATRIX_VP" : "UNITY_MATRIX_MVP"));
        }

        public override void WriteFunctions(ShaderSourceBuilder builder, ShaderMetaData data)
        {
            if (m_HasFlipBook)
            {
                builder.WriteLine("float2 GetSubUV(int flipBookIndex,float2 uv,float2 dim,float2 invDim)");
                builder.EnterScope();
                builder.WriteLine("float2 tile = float2(fmod(flipBookIndex,dim.x),dim.y - 1.0 - floor(flipBookIndex * invDim.x));");
                builder.WriteLine("return (tile + uv) * invDim;");
                builder.ExitScope();
                builder.WriteLine();
            }
        }

        private static void WriteTex2DFetch(ShaderSourceBuilder builder, ShaderMetaData data, VFXValue texture, string uv, bool endLine)
        {
            builder.WriteFormat("{0}Texture.Sample(sampler{0}Texture,{1})",data.paramToName[(int)ShaderMetaData.Pass.kOutput][texture],uv);
            if (endLine)
                builder.WriteLine(";");
        }

        public override void WritePixelShader(ShaderSourceBuilder builder, ShaderMetaData data)
        {
            if (!m_HasTexture)
            {
                builder.WriteLine("float lsqr = dot(i.offsets, i.offsets);");
                builder.WriteLine("if (lsqr > 1.0)");
                builder.WriteLine("\tdiscard;");
                builder.WriteLine();
            }
            else if (m_HasFlipBook)
            {
                builder.Write("float2 dim = ");
                builder.Write(data.paramToName[(int)ShaderMetaData.Pass.kOutput][m_Values[FlipbookDimIndex]]);
                builder.WriteLine(";");
                builder.WriteLine("float2 invDim = 1.0 / dim; // TODO InvDim should be computed on CPU");

                if (!m_HasMotionVectors)
                {
                    const bool INTERPOLATE = true; // TODO Add a toggle on block

                    if (!INTERPOLATE)
                    {
                        builder.WriteLine("float2 uv = GetSubUV(i.flipbookIndex,i.offsets.xy,dim,invDim);");
                        builder.Write("color *= ");
                        WriteTex2DFetch(builder, data, m_Values[TextureIndex], "uv", true);
                    }
                    else
                    {
                        builder.WriteLine("float ratio = frac(i.flipbookIndex);");
                        builder.WriteLine("float index = i.flipbookIndex - ratio;");
                        builder.WriteLine();

                        builder.WriteLine("float2 uv1 = GetSubUV(index,i.offsets.xy,dim,invDim);");
                        builder.Write("float4 col1 = ");
                        WriteTex2DFetch(builder, data, m_Values[TextureIndex], "uv1", true);
                        builder.WriteLine();

                        builder.WriteLine("float2 uv2 = GetSubUV(index + 1.0,i.offsets.xy,dim,invDim);");
                        builder.Write("float4 col2 = ");
                        WriteTex2DFetch(builder, data, m_Values[TextureIndex], "uv2", true);
                        builder.WriteLine();

                        builder.WriteLine("color *= lerp(col1,col2,ratio);");
                    }
                }
                else
                {
                    builder.WriteLine("float ratio = frac(i.flipbookIndex);");
                    builder.WriteLine("float index = i.flipbookIndex - ratio;");
                    builder.WriteLine();

                    builder.WriteLine("float2 uv1 = GetSubUV(index,i.offsets.xy,dim,invDim);");
                    builder.Write("float2 duv1 = ");
                    WriteTex2DFetch(builder, data, m_Values[MorphTextureIndex], "uv1", false);
                    builder.WriteLine(".rg - 0.5;");
                    builder.WriteLine();

                    builder.WriteLine("float2 uv2 = GetSubUV(index + 1.0,i.offsets.xy,dim,invDim);");
                    builder.Write("float2 duv2 = ");
                    WriteTex2DFetch(builder, data, m_Values[MorphTextureIndex], "uv2", false);
                    builder.WriteLine(".rg - 0.5;");
                    builder.WriteLine();

                    builder.Write("float morphIntensity = ");
                    builder.Write(data.paramToName[(int)ShaderMetaData.Pass.kOutput][m_Values[MorphIntensityIndex]]);
                    builder.WriteLine(";");
                    builder.WriteLine("duv1 *= morphIntensity * ratio;");
                    builder.WriteLine("duv2 *= morphIntensity * (ratio - 1.0);");
                    builder.WriteLine();

                    builder.Write("float4 col1 = ");
                    WriteTex2DFetch(builder, data, m_Values[TextureIndex], "uv1 - duv1", true);
                    builder.Write("float4 col2 = ");
                    WriteTex2DFetch(builder, data, m_Values[TextureIndex], "uv2 - duv2", true);
                    builder.WriteLine();

                    builder.WriteLine("color *= lerp(col1,col2,ratio);");
                }

            }
            else
            {
                builder.Write("color *= ");
                WriteTex2DFetch(builder, data, m_Values[TextureIndex], "i.offsets", true);
            }

            if (data.system.BlendingMode == BlendMode.kMasked)
                builder.WriteLine("if (color.a < 0.33333) discard;");
            else if (data.system.BlendingMode == BlendMode.kDithered)
            {
                // bayer
                builder.WriteLine("const float kernel[16] = {1,9,3,11,13,5,15,7,4,12,2,10,16,8,14,6};");
                // half toning
                //builder.WriteLine("const float kernel[16] = {7,8,9,10,6,1,2,11,5,4,3,12,16,15,14,13};");

                builder.WriteLine("int kernelIndex = (((int)i.pos.y & 3) << 2) + ((int)i.pos.x & 3);");
                builder.WriteLine("clip(color.a  - kernel[kernelIndex] / 17.0f);");
            }
        }

        private VFXValue[] m_Values = new VFXValue[4];

        private bool m_HasSize;
        private bool m_HasAngle;
        private bool m_HasFlipBook;
        private bool m_HasTexture;
        private OrientMode m_OrientMode;
        private bool m_HasMotionVectors;
        private bool m_HasPivot;
        private bool m_UseSoftParticles;
        private bool m_HasFront;
        private bool m_HasSide;
        private bool m_HasUp;
    }

    // Experimental
    public class VFXSphereOutputShaderGeneratorModule : VFXOutputShaderGeneratorModule
    {
        public VFXSphereOutputShaderGeneratorModule(VFXPropertySlot[] slots)
        {
            m_Values[0] = slots[0].ValueRef.Reduce() as VFXValue;
            m_Values[1] = slots[1].ValueRef.Reduce() as VFXValue;
        }

        public override bool UpdateAttributes(Dictionary<VFXAttribute, VFXAttribute.Usage> attribs, ref VFXBlockDesc.Flag flags)
        {
            if (!UpdateFlag(attribs, CommonAttrib.Position, VFXContextDesc.Type.kTypeOutput))
            {
                Debug.LogError("Position attribute is needed for point output context");
                return false;
            }

            UpdateFlag(attribs, CommonAttrib.Color, VFXContextDesc.Type.kTypeOutput);
            m_HasSize = UpdateFlag(attribs, CommonAttrib.Size, VFXContextDesc.Type.kTypeOutput);
            return true;
        }

        public override void UpdateUniforms(HashSet<VFXExpression> uniforms, ref VFXBlockDesc.Flag flags)
        {
            uniforms.Add(m_Values[MetallicSlot]);
            uniforms.Add(m_Values[SmoothnessSlot]);
        }

        public override int[] GetSingleIndexBuffer(ShaderMetaData data) { return new int[0]; } // tmp

        public override int GetOutputType()
        {
            return 1;
        }

        public override void WriteIndex(ShaderSourceBuilder builder, ShaderMetaData data)
        {
            builder.WriteLine("uint index = (id >> 2) + instanceID * 2048;");
        }

        public override void WriteAdditionalVertexOutput(ShaderSourceBuilder builder, ShaderMetaData data)
        {
            builder.WriteLine("float2 offsets : TEXCOORD0;");
            builder.WriteLine("nointerpolation float3 viewCenterPos : TEXCOORD1;");
            builder.WriteLine("float4 viewPosAndSize : TEXCOORD2;");
        }

        public override void WriteAdditionalPixelOutput(ShaderSourceBuilder builder, ShaderMetaData data)
        {
            builder.WriteLine("float4 spec_smoothness : SV_Target1;");
            builder.WriteLine("float4 normal : SV_Target2;");
            builder.WriteLine("float4 emission : SV_Target3;");
            builder.WriteLine("float depth : SV_DepthLessEqual;");
        }

        public override bool CanUseDeferred() { return true; }

        public override void WritePostBlock(ShaderSourceBuilder builder, ShaderMetaData data)
        {
            if (m_HasSize)
            {
                builder.Write("float2 size = ");
                builder.WriteAttrib(CommonAttrib.Size, data, ShaderMetaData.Pass.kOutput);
                builder.WriteLine(" * 0.5f;");
            }
            else
                builder.WriteLine("float2 size = float2(0.005,0.005);");

            builder.WriteLine("o.offsets.x = 2.0 * float(id & 1) - 1.0;");
            builder.WriteLine("o.offsets.y = 2.0 * float((id & 2) >> 1) - 1.0;");
            builder.WriteLine();

            builder.Write("float3 position = ");
            builder.WriteAttrib(CommonAttrib.Position, data, ShaderMetaData.Pass.kOutput);
            builder.WriteLine(";");
            builder.WriteLine();

            builder.WriteLine("float3 posToCam = VFXCameraPos() - position;");
            builder.WriteLine("float camDist = length(posToCam);");
            builder.WriteLine("float scale = 1.0f - size.x / camDist;");
            builder.WriteLine("float3 front = posToCam / camDist;");
            builder.WriteLine("float3 side = normalize(cross(front,VFXCameraMatrix()[1].xyz));");
            builder.WriteLine("float3 up = cross(side,front);");

            builder.WriteLine();

            builder.WriteLineFormat("o.viewCenterPos = mul({0},float4(position,1.0f)).xyz;", (data.system.WorldSpace ? "UNITY_MATRIX_V" : "UNITY_MATRIX_MV"));

            builder.WriteLine("position += side * (o.offsets.x * size.x) * scale;");
            builder.WriteLine("position += up * (o.offsets.y * size.y) * scale;");
            builder.WriteLine("position += front * size.x;");

            builder.WriteLine();

            builder.WriteLineFormat("o.viewPosAndSize = float4(mul({0},float4(position,1.0f)).xyz,size.x);", (data.system.WorldSpace ? "UNITY_MATRIX_V" : "UNITY_MATRIX_MV"));
            builder.WriteLineFormat("o.pos = mul ({0}, float4(position,1.0f));", (data.system.WorldSpace ? "UNITY_MATRIX_VP" : "UNITY_MATRIX_MVP"));
        }

        public override void WritePixelShader(ShaderSourceBuilder builder, ShaderMetaData data)
        {
            builder.WriteLine("float lsqr = dot(i.offsets, i.offsets);");
            builder.WriteLine("if (lsqr > 1.0)");
            builder.WriteLine("\tdiscard;");
            builder.WriteLine();

            builder.WriteLine("float nDepthOffset = 1.0f - sqrt(1.0f - lsqr); // normalized depth offset");

            builder.WriteLine("float3 camToPosDir = normalize(i.viewPosAndSize.xyz);");
            builder.WriteLine("float3 viewPos = i.viewPosAndSize.xyz + (camToPosDir * (nDepthOffset * i.viewPosAndSize.w));");
            //builder.WriteLine("float depth = -viewPos.z;");
            builder.WriteLine("o.depth = -(1.0f + viewPos.z * _ZBufferParams.w) / (viewPos.z * _ZBufferParams.z);");

            builder.WriteLine("float3 specColor = (float3)0;");
            builder.WriteLine("float oneMinusReflectivity = 0;");
            builder.WriteLineFormat("float metalness = saturate({0});", data.paramToName[(int)ShaderMetaData.Pass.kOutput][m_Values[MetallicSlot]]);
            builder.WriteLine("color.rgb = DiffuseAndSpecularFromMetallic(color.rgb,metalness,specColor,oneMinusReflectivity);");

            builder.WriteLine("color.a = 0.0f;"); // occlusion

            //builder.WriteLine("float3 normal = float3(i.offsets.x,i.offsets.y,nDepthOffset - 1.0f);");
            builder.WriteLine("float3 normal = normalize(viewPos - i.viewCenterPos) * float3(1,1,-1);");
            //builder.WriteLine("color.xyz = mul(unity_CameraToWorld, float4(normal,0.0f)).xyz * 0.5f + 0.5f;");
            builder.WriteLineFormat("o.spec_smoothness = float4(specColor,{0});",data.paramToName[(int)ShaderMetaData.Pass.kOutput][m_Values[SmoothnessSlot]]);
            builder.WriteLine("o.normal = mul(unity_CameraToWorld, float4(normal,0.0f)) * 0.5f + 0.5f;"); // -float4(normal,0.0f) * 0.5f + 0.5f;//
            //builder.WriteLine("color = (float4)0.0f;"); // occlusion

            builder.WriteLine("half3 ambient = color.xyz * 0.0f;//ShadeSHPerPixel(normal, float4(color.xyz, 1) * 0.1, float3(0, 0, 0));");
            builder.WriteLine("o.emission = float4(ambient, 0);");
        }

        public const int MetallicSlot = 0;
        public const int SmoothnessSlot = 1;

        private VFXValue[] m_Values = new VFXValue[2];
        private bool m_HasSize;
    }
}

