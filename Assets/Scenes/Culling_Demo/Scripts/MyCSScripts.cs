using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[ExecuteInEditMode]
public class MyCSScripts : MonoBehaviour
{

    public ComputeShader _computeShader;
    public Material _mat;
    
    // Start is called before the first frame update
    void Start()
    {
        RenderTexture mRenderTexture = new RenderTexture(2048, 2048, 16);
        mRenderTexture.enableRandomWrite = true;
        mRenderTexture.Create();
        
        _mat.mainTexture = mRenderTexture;
        
        _computeShader.SetTexture(0, "Result", mRenderTexture);
        int kernelIndex = _computeShader.FindKernel("CSMain");
        
        _computeShader.Dispatch(kernelIndex, 2048 / 8, 2048 / 8, 1);
    }

    // Update is called once per frame
    void Update()
    {
    }
}
