// Copyright (c) Microsoft Corporation.  All rights reserved.

#include"DXGI/DXGIAdapter.h"
#include"DXGI/DXGIAdapter1.h"
#include"DXGI/DXGIDevice.h"
#include"DXGI/DXGIDevice1.h"
#include"DXGI/DXGIDeviceSubObject.h"
#include"DXGI/DXGIFactory.h"
#include"DXGI/DXGIFactory1.h"
#include"DXGI/DXGIKeyedMutex.h"
#include"DXGI/DXGIObject.h"
#include"DXGI/DXGIOutput.h"
#include"DXGI/DXGIResource.h"
#include"DXGI/DXGISurface.h"
#include"DXGI/DXGISurface1.h"
#include"DXGI/DXGISwapChain.h"
#include"Direct3D10/D3D10BlendState.h"
#include"Direct3D10/D3D10BlendState1.h"
#include"Direct3D10/D3D10Blob.h"
#include"Direct3D10/D3D10Buffer.h"
#include"Direct3D10/D3D10Debug.h"
#include"Direct3D10/D3D10DepthStencilState.h"
#include"Direct3D10/D3D10DepthStencilView.h"
#include"Direct3D10/D3D10Device.h"
#include"Direct3D10/D3D10Device1.h"
#include"Direct3D10/D3D10DeviceChild.h"
#include"Direct3D10/D3D10Effect.h"
#include"Direct3D10/D3D10EffectBlendVariable.h"
#include"Direct3D10/D3D10EffectConstantBuffer.h"
#include"Direct3D10/D3D10EffectDepthStencilVariable.h"
#include"Direct3D10/D3D10EffectDepthStencilViewVariable.h"
#include"Direct3D10/D3D10EffectMatrixVariable.h"
#include"Direct3D10/D3D10EffectPass.h"
#include"Direct3D10/D3D10EffectPool.h"
#include"Direct3D10/D3D10EffectRasterizerVariable.h"
#include"Direct3D10/D3D10EffectRenderTargetViewVariable.h"
#include"Direct3D10/D3D10EffectSamplerVariable.h"
#include"Direct3D10/D3D10EffectScalarVariable.h"
#include"Direct3D10/D3D10EffectShaderResourceVariable.h"
#include"Direct3D10/D3D10EffectShaderVariable.h"
#include"Direct3D10/D3D10EffectStringVariable.h"
#include"Direct3D10/D3D10EffectTechnique.h"
#include"Direct3D10/D3D10EffectType.h"
#include"Direct3D10/D3D10EffectVariable.h"
#include"Direct3D10/D3D10EffectVectorVariable.h"
#include"Direct3D10/D3D10GeometryShader.h"
#include"Direct3D10/D3D10Include.h"
#include"Direct3D10/D3D10InputLayout.h"
#include"Direct3D10/D3D10Multithread.h"
#include"Direct3D10/D3D10PixelShader.h"
#include"Direct3D10/D3D10RasterizerState.h"
#include"Direct3D10/D3D10RenderTargetView.h"
#include"Direct3D10/D3D10Resource.h"
#include"Direct3D10/D3D10SamplerState.h"
#include"Direct3D10/D3D10ShaderReflection.h"
#include"Direct3D10/D3D10ShaderReflection1.h"
#include"Direct3D10/D3D10ShaderReflectionConstantBuffer.h"
#include"Direct3D10/D3D10ShaderReflectionType.h"
#include"Direct3D10/D3D10ShaderReflectionVariable.h"
#include"Direct3D10/D3D10ShaderResourceView.h"
#include"Direct3D10/D3D10ShaderResourceView1.h"
#include"Direct3D10/D3D10StateBlock.h"
#include"Direct3D10/D3D10SwitchToRef.h"
#include"Direct3D10/D3D10Texture1D.h"
#include"Direct3D10/D3D10Texture2D.h"
#include"Direct3D10/D3D10Texture3D.h"
#include"Direct3D10/D3D10VertexShader.h"
#include"Direct3D10/D3D10View.h"
#include"Direct3D11/D3D11BlendState.h"
#include"Direct3D11/D3D11Buffer.h"
#include"Direct3D11/D3D11ClassInstance.h"
#include"Direct3D11/D3D11ClassLinkage.h"
#include"Direct3D11/D3D11CommandList.h"
#include"Direct3D11/D3D11ComputeShader.h"
#include"Direct3D11/D3D11Debug.h"
#include"Direct3D11/D3D11DepthStencilState.h"
#include"Direct3D11/D3D11DepthStencilView.h"
#include"Direct3D11/D3D11Device.h"
#include"Direct3D11/D3D11DeviceChild.h"
#include"Direct3D11/D3D11DeviceContext.h"
#include"Direct3D11/D3D11DomainShader.h"
#include"Direct3D11/D3D11GeometryShader.h"
#include"Direct3D11/D3D11HullShader.h"
#include"Direct3D11/D3D11InputLayout.h"
#include"Direct3D11/D3D11PixelShader.h"
#include"Direct3D11/D3D11RasterizerState.h"
#include"Direct3D11/D3D11RenderTargetView.h"
#include"Direct3D11/D3D11Resource.h"
#include"Direct3D11/D3D11SamplerState.h"
#include"Direct3D11/D3D11ShaderResourceView.h"
#include"Direct3D11/D3D11SwitchToRef.h"
#include"Direct3D11/D3D11Texture1D.h"
#include"Direct3D11/D3D11Texture2D.h"
#include"Direct3D11/D3D11Texture3D.h"
#include"Direct3D11/D3D11UnorderedAccessView.h"
#include"Direct3D11/D3D11VertexShader.h"
#include"Direct3D11/D3D11View.h"
