using System;
using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.Rendering;
using Random = UnityEngine.Random;

public class ExampleClass : MonoBehaviour
{
    const RenderTextureFormat m_depthTextureFormat = RenderTextureFormat.RHalf;//深度取值范围0-1，单通道即可。
    
    public int instanceCount = 100000;
    public Mesh instanceMesh;
    public Material instanceMaterial;
    public int subMeshIndex = 0;
    public ComputeShader compute;
    public GameObject quad;
    public RenderTexture depthTexture => m_depthTexture;
    public Shader depthTextureShader;
    
    public DepthTextureGenerator depthTextureGenerator;
    
    public int depthTextureSize
    {
        get
        {
            if(m_depthTextureSize == 0)
            {
                m_depthTextureSize = Mathf.NextPowerOfTwo(Mathf.CeilToInt(Mathf.Sqrt(instanceCount)));
            }
            return m_depthTextureSize;
        }
        
    }

    private int cachedInstanceCount = -1;
    private int cachedSubMeshIndex = -1;
    private int kernel;
    private ComputeBuffer positionBuffer;
    private ComputeBuffer argsBuffer;
    private ComputeBuffer localToWorldMatrixBuffer;
    private ComputeBuffer cullResultBuffer;
    private List<Matrix4x4> localToWorldMatrixs = new List<Matrix4x4>();
    private Camera camera;
    private uint[] args = new uint[5] { 0, 0, 0, 0, 0 };

    private RenderTexture m_depthTexture;
    private int m_depthTextureSize = 0;
    
    private Material m_depthTextureMaterial;
    private int m_depthTextureShaderID;
    
    private void InitDepthTexture() {
        if(m_depthTexture != null) return;
        m_depthTexture = new RenderTexture(depthTextureSize, depthTextureSize, 0, m_depthTextureFormat);
        m_depthTexture.autoGenerateMips = false;//Mipmap手动生成
        m_depthTexture.useMipMap = true;
        m_depthTexture.filterMode = FilterMode.Point;
        m_depthTexture.Create();
    }
    
    // private void OnEndCameraRendering(ScriptableRenderContext arg1, Camera arg2)
    // {
    //     //OnPostRender();
    // }

    #region Functions

    private Vector4 GetPlane(Vector3 normal, Vector3 point)
    {
        // Plane equation: Ax + By + Cz + D = 0
        // where (A, B, C) is the normal vector and D is the distance from the origin
        return new Vector4(normal.x, normal.y, normal.z, -Vector3.Dot(normal, point));
    }

    private Vector4 GetPlane(Vector3 a, Vector3 b, Vector3 c)
    {
        Vector3 normal = Vector3.Cross(b - a, c - a).normalized;
        return GetPlane(normal, a);
    }

    private Vector3[] GetCameraFarClipPlanePoints(Camera camera)
    {
        Vector3[] points = new Vector3[4];
        Transform transform = camera.transform;
        float distance = camera.farClipPlane;
        float halfFovRad = Mathf.Deg2Rad * camera.fieldOfView * 0.5f;
        float upLen = distance * Mathf.Tan(halfFovRad);
        float rightLen = upLen * camera.aspect;
        Vector3 farCenterPoint = transform.position + distance * transform.forward;
        Vector3 up = upLen * transform.up;
        Vector3 right = rightLen * transform.right;
        points[0] = farCenterPoint - up - right; //left-bottom
        points[1] = farCenterPoint - up + right; //right-bottom
        points[2] = farCenterPoint + up - right; //left-up
        points[3] = farCenterPoint + up + right; //right-up
        return points;
    }

    public Vector4[] GetFrustumPlane(Camera camera)
    {
        Vector4[] planes = new Vector4[6];
        Transform transform = camera.transform;
        Vector3 cameraPosition = transform.position;
        Vector3[] points = GetCameraFarClipPlanePoints(camera);
        //顺时针
        planes[0] = GetPlane(cameraPosition, points[0], points[2]); //left
        planes[1] = GetPlane(cameraPosition, points[3], points[1]); //right
        planes[2] = GetPlane(cameraPosition, points[1], points[0]); //bottom
        planes[3] = GetPlane(cameraPosition, points[2], points[3]); //up
        planes[4] = GetPlane(-transform.forward, transform.position + transform.forward * camera.nearClipPlane); //near
        planes[5] = GetPlane(transform.forward, transform.position + transform.forward * camera.farClipPlane); //far
        return planes;
    }
    
    #endregion

    // private void OnEnable()
    // {
    //     RenderPipelineManager.endCameraRendering += OnEndCameraRendering;
    // }

    void Start()
    {
        // Camera.main.depthTextureMode |= DepthTextureMode.Depth;
        // m_depthTextureMaterial  = new  Material(depthTextureShader);
        // m_depthTextureShaderID = Shader.PropertyToID("_CameraDepthTexture");
        // InitDepthTexture();
        
        camera = Camera.main;
        kernel = compute.FindKernel("ViewPortCulling");
        cullResultBuffer = new ComputeBuffer(instanceCount, sizeof(float)*16, ComputeBufferType.Append);
        argsBuffer = new ComputeBuffer(1, args.Length * sizeof(uint), ComputeBufferType.IndirectArguments);
        UpdateBuffers();
    }

    void Update()
    {
        // Update starting position buffer
        if (cachedInstanceCount != instanceCount || cachedSubMeshIndex != subMeshIndex)
            UpdateBuffers();
        
        //Vector4[] planes = GetFrustumPlane(camera);
        
        compute.SetBuffer(kernel, "input", localToWorldMatrixBuffer);
        cullResultBuffer.SetCounterValue(0);
        compute.SetBuffer(kernel, "cullresult", cullResultBuffer);
        compute.SetInt("instanceCount", instanceCount);
        //compute.SetVectorArray("planes", planes);
        compute.SetBool("isOpenGL", false);
        compute.SetMatrix("vpMatrix", camera.projectionMatrix * camera.worldToCameraMatrix);
        compute.SetMatrix("vpMatrix", GL.GetGPUProjectionMatrix(camera.projectionMatrix, false) * camera.worldToCameraMatrix);
        compute.SetInt("depthTextureSize",depthTextureGenerator.depthTextureSize);
        compute.SetTexture(kernel, "hizTexture", depthTextureGenerator.depthTexture);
        
        compute.Dispatch(kernel, 1 + (instanceCount / 640), 1, 1);
        
        instanceMaterial.SetBuffer("positionBuffer", cullResultBuffer);

        ComputeBuffer.CopyCount(cullResultBuffer, argsBuffer, sizeof(uint));
        
        // Render
        Graphics.DrawMeshInstancedIndirect(instanceMesh, subMeshIndex, instanceMaterial,
            new Bounds(Vector3.zero, new Vector3(100.0f, 100.0f, 100.0f)), argsBuffer);
    }

    void UpdateBuffers()
    {
        // Ensure submesh index is in range
        if (instanceMesh != null)
            subMeshIndex = Mathf.Clamp(subMeshIndex, 0, instanceMesh.subMeshCount - 1);
        
        if(localToWorldMatrixBuffer!=null)
            localToWorldMatrixBuffer.Release();
        
        localToWorldMatrixBuffer = new ComputeBuffer(instanceCount, sizeof(float) * 16);
        localToWorldMatrixs.Clear();
        
        // Positions
        if (positionBuffer != null)
            positionBuffer.Release();
        positionBuffer = new ComputeBuffer(instanceCount, 16);
        Vector4[] positions = new Vector4[instanceCount];
        for (int i = 0; i < instanceCount; i++)
        {
            float angle = Random.Range(0.0f, Mathf.PI * 2.0f);
            float distance = Random.Range(20.0f, 100.0f);
            float height = Random.Range(-2.0f, 2.0f);
            float size = Random.Range(0.05f, 0.25f);
            positions[i] = new Vector4(Mathf.Sin(angle) * distance, height, Mathf.Cos(angle) * distance, size);
            localToWorldMatrixs.Add(Matrix4x4.TRS(positions[i], Quaternion.identity, new Vector3(size, size, size)));
        }
        localToWorldMatrixBuffer.SetData(localToWorldMatrixs);
        //positionBuffer.SetData(positions);
        //instanceMaterial.SetBuffer("positionBuffer", localToWorldMatrixBuffer);

        // Indirect args
        if (instanceMesh != null)
        {
            args[0] = (uint)instanceMesh.GetIndexCount(subMeshIndex);
            args[1] = (uint)instanceCount;
            args[2] = (uint)instanceMesh.GetIndexStart(subMeshIndex);
            args[3] = (uint)instanceMesh.GetBaseVertex(subMeshIndex);
        }
        else
        {
            args[0] = args[1] = args[2] = args[3] = 0;
        }

        argsBuffer.SetData(args);

        cachedInstanceCount = instanceCount;
        cachedSubMeshIndex = subMeshIndex;
    }

    void OnDisable()
    {
        //RenderPipelineManager.endCameraRendering -= OnEndCameraRendering;
        
        localToWorldMatrixBuffer?.Release();
        localToWorldMatrixBuffer = null;
        
        cullResultBuffer?.Release();
        cullResultBuffer = null;
        
        if (positionBuffer != null)
            positionBuffer.Release();
        positionBuffer = null;

        if (argsBuffer != null)
            argsBuffer.Release();
        argsBuffer = null;
    }

    // void OnPostRender()
    // {
    //     int w = m_depthTexture.width;
    //     int mipmapLevel = 0;
    //
    //     RenderTexture currentRenderTexture = null;//当前mipmapLevel对应的mipmap
    //     RenderTexture preRenderTexture = null;//上一层的mipmap，即mipmapLevel-1对应的mipmap
    //
    //     //如果当前的mipmap的宽高大于8，则计算下一层的mipmap
    //     while(w > 8) {
    //         currentRenderTexture = RenderTexture.GetTemporary(w, w, 0, m_depthTextureFormat);
    //         currentRenderTexture.filterMode = FilterMode.Point;
    //         if(preRenderTexture == null) {
    //             //Mipmap[0]即copy原始的深度图
    //             Graphics.Blit(Shader.GetGlobalTexture(m_depthTextureShaderID), currentRenderTexture);
    //         }
    //         else {
    //             //将Mipmap[i] Blit到Mipmap[i+1]上
    //             Graphics.Blit(preRenderTexture, currentRenderTexture, m_depthTextureMaterial);
    //             RenderTexture.ReleaseTemporary(preRenderTexture);
    //         }
    //         Graphics.CopyTexture(currentRenderTexture, 0, 0, m_depthTexture, 0, mipmapLevel);
    //         preRenderTexture = currentRenderTexture;
    //
    //         w /= 2;
    //         mipmapLevel++;
    //     }
    //     RenderTexture.ReleaseTemporary(preRenderTexture);
    // }
}