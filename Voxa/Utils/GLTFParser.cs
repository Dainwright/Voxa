﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using OpenTK;
using OpenTK.Graphics;
using OpenTK.Graphics.OpenGL;
using Voxa.Objects;
using Voxa.Rendering;

namespace Voxa.Utils
{
    public class GLTFLoader
    {
        // See https://github.com/javagl/gltfOverview/releases
        readonly JObject _json;
        readonly Dictionary<int, BinaryReader> _buffers = new Dictionary<int, BinaryReader>();
        readonly string _resourcePath;

        public GLTFLoader(string resourcePath, string resourceName)
        {
            _resourcePath = resourcePath;
            _json = JObject.Parse(ResourceManager.GetTextResource(_resourcePath + "." + resourceName));

            if ((string)_json["asset"]["version"] != "2.0") throw new Exception("Unsupported GLTF format version (expected 2.0)");

            if (_json["scenes"].Count() != 1) throw new Exception("Expected a single scene");
            int sceneId = (int)_json["scene"];
            var sceneJson = _json["scenes"][sceneId];

            for (int i = 0; i < _json["buffers"].Count(); i++)
            {
                _buffers[i] = ResourceManager.GetBinaryResourceReader(_resourcePath + "." + (string)_json["buffers"][i]["uri"]);
            }
        }

        public List<Material> GetAllMaterials()
        {
            List<Material> materials = new List<Material>();

            JArray materialsJson = (JArray)(_json["materials"]);
            for (int i = 0; i < materialsJson.Count; i++) {
                materials.Add(this.GetMaterial(i));
            }

            return materials;
        }

        public Material GetMaterial(int materialId)
        {
            Material material = new Material(materialId);
            JObject materialJson = (JObject)_json["materials"][materialId];
            if (materialJson != null) {
                if (materialJson["name"] != null) {
                    material.Name = (string)materialJson["name"];
                }

                if (materialJson["extensions"] != null && materialJson["extensions"]["KHR_materials_common"] != null) {
                    JObject materialInfo = (JObject)materialJson["extensions"]["KHR_materials_common"]["values"];
                    if ((string)materialJson["extensions"]["KHR_materials_common"]["technique"] != "PHONG") {
                        Logger.Warning($"Voxa doesn't support {(string)materialInfo["technique"]} shader technique. Phong shading technique will be used instead.");
                    }
                    JArray diffuse = (JArray)materialInfo["diffuse"];
                    if (diffuse != null && diffuse.Count == 1) { // Diffuse texture
                        string textureResourcePath = this.getTextureResourcePath((int)diffuse[0]);
                        material.DiffuseMap = new Texture(textureResourcePath);
                    } else if (diffuse != null && diffuse.Count >= 3) { // Diffuse color
                        float alpha = (diffuse[3] != null) ? (float)diffuse[3] : 1;
                        material.DiffuseColor = new Color4((float)diffuse[0], (float)diffuse[1], (float)diffuse[2], alpha);
                    } else {
                        material.DiffuseColor = Color4.White;
                    }
                    JArray specular = (JArray)materialInfo["specular"];
                    if (specular != null && specular.Count == 1) { // Specular texture
                        string textureResourcePath = this.getTextureResourcePath((int)specular[0]);
                        material.SpecularMap = new Texture(textureResourcePath);
                        material.SpecularMap.Unit = TextureUnit.Texture1;
                    } else if (specular != null && specular.Count >= 3) { // Specular color
                        float alpha = (specular[3] != null) ? (float)specular[3] : 1;
                        material.SpecularColor = new Color4((float)specular[0], (float)specular[1], (float)specular[2], alpha);
                    } else {
                        material.SpecularColor = Color4.White;
                    }
                    JArray ambient = (JArray)materialInfo["ambient"];
                    if (ambient != null && ambient.Count == 4) {
                        float alpha = (ambient[3] != null) ? (float)ambient[3] : 1;
                        material.AmbientColor = new Color4((float)ambient[0], (float)ambient[1], (float)ambient[2], alpha);
                    } else {
                        material.AmbientColor = (material.DiffuseColor.A > 0) ? material.DiffuseColor : Color4.White;
                    }
                    if (materialInfo["shininess"] != null)
                        material.Shininess = (float)materialInfo["shininess"][0];
                } else if (materialJson["pbrMetallicRoughness"] != null) {
                    JObject materialInfo = (JObject)materialJson["pbrMetallicRoughness"];
                    if (materialInfo["baseColorTexture"] != null) {
                        string textureResourcePath = this.getTextureResourcePath((int)materialInfo["baseColorTexture"]["index"]);
                        material.DiffuseMap = new Texture(textureResourcePath);
                        material.SpecularColor = Color4.White;
                    } else if (materialInfo["baseColorFactor"] != null) {
                        JArray color = (JArray)materialJson["pbrMetallicRoughness"]["baseColorFactor"];
                        float alpha = (color[3] != null) ? (float)color[3] : 1;
                        material.DiffuseColor = new Color4((float)color[0], (float)color[1], (float)color[2], alpha);
                        material.SpecularColor = material.DiffuseColor;
                    }
                    material.AmbientColor = material.SpecularColor;
                    material.Shininess = 16;
                } else {
                    Logger.Error($"{materialId} Material type not supported");
                    return null;
                }

                return material;
            }
            return null;
        }

        public List<Mesh> GetAllMeshes()
        {
            List<Mesh> meshes = new List<Mesh>();

            for (int i = 0; i < _json["nodes"].Count(); i++) {
                if (_json["nodes"][i]["mesh"] != null) {
                    meshes.Add(this.getMesh((JObject)_json["nodes"][i]));
                }
            }

            return meshes;
        }

        public Mesh GetMesh(string nodeName)
        {
            JObject nodeJson = null;

            for (int i = 0; i < _json["nodes"].Count(); i++) {
                if (_json["nodes"][i]["name"] != null && (string)_json["nodes"][i]["name"] == nodeName) {
                    nodeJson = (JObject)_json["nodes"][i];
                }
            }

            if (nodeJson == null)
                throw new Exception($"No node found named {nodeName}.");

            return this.getMesh(nodeJson);
        }

        public Mesh GetMesh(int nodeId)
        {
            JObject nodeJson = (JObject)_json["nodes"][nodeId];

            if (nodeJson == null)
                throw new Exception($"No node found with id {nodeId}.");

            return this.getMesh(nodeJson);
        }

        private Mesh getMesh(JObject nodeJson)
        {
            int meshId = (int)nodeJson["mesh"];
            JObject meshJson = (JObject)_json["meshes"][meshId];

            List<Mesh.Primitive> primitives = new List<Mesh.Primitive>();
            foreach (JObject primitiveJson in (JArray)meshJson["primitives"]) {
                
                // Material
                int materialId = (int)primitiveJson["material"];

                // Geometry
                if (primitiveJson["mode"] == null || (GLTFPrimitiveMode)(int)primitiveJson["mode"] != GLTFPrimitiveMode.TRIANGLES) {
                    throw new Exception($"Found unsupported primitive mode {(int)primitiveJson["mode"]} for {meshId}, only TRIANGLES (4) is supported.");
                }

                int positionAttributeAccessorId = (int)primitiveJson["attributes"]["POSITION"];
                var positionAccessor = _json["accessors"][positionAttributeAccessorId];

                int verticesCount = (int)positionAccessor["count"];

                TexturedVertex[] staticVertices = new TexturedVertex[verticesCount];

                JToken accessor, bufferView;
                int sourceOffset, stride;
                BinaryReader buffer;

                // Position
                accessor = positionAccessor;
                if ((GLTFConst)(int)accessor["componentType"] != GLTFConst.FLOAT) {
                    throw new Exception($"Found unexpected component type {(int)accessor["componentType"]} for POSITION attribute of {meshId}, FLOAT was expected.");
                }

                bufferView = _json["bufferViews"][(int)accessor["bufferView"]];
                int accessorByteOffset = (accessor["byteOffset"] != null) ? (int)accessor["byteOffset"] : 0;
                int bufferViewByteOffset = (bufferView["byteOffset"] != null) ? (int)bufferView["byteOffset"] : 0;
                sourceOffset = bufferViewByteOffset + accessorByteOffset;
                stride = (bufferView["byteStride"] != null) ? (int)bufferView["byteStride"] : sizeof(float) * 3;

                buffer = _buffers[(int)bufferView["buffer"]];

                for (var i = 0; i < verticesCount; i++) {
                    buffer.BaseStream.Position = sourceOffset + stride * i;

                    var position = new Vector3(buffer.ReadSingle(), buffer.ReadSingle(), buffer.ReadSingle());
                    staticVertices[i].Position = position;
                    staticVertices[i].Color = Color4.White;
                }

                // Normals
                int normalAttributeAccessorId = (int)primitiveJson["attributes"]["NORMAL"];
                var normalAccessor = _json["accessors"][normalAttributeAccessorId];
                accessor = normalAccessor;
                if ((GLTFConst)(int)accessor["componentType"] != GLTFConst.FLOAT) {
                    throw new Exception($"Found unexpected component type {(int)accessor["componentType"]} for NORMAL attribute of {meshId}, FLOAT was expected.");
                }

                bufferView = _json["bufferViews"][(int)accessor["bufferView"]];
                accessorByteOffset = (accessor["byteOffset"] != null) ? (int)accessor["byteOffset"] : 0;
                bufferViewByteOffset = (bufferView["byteOffset"] != null) ? (int)bufferView["byteOffset"] : 0;
                sourceOffset = bufferViewByteOffset + accessorByteOffset;
                stride = (bufferView["byteStride"] != null) ? (int)bufferView["byteStride"] : sizeof(float) * 3;

                buffer = _buffers[(int)bufferView["buffer"]];

                for (var i = 0; i < verticesCount; i++) {
                    buffer.BaseStream.Position = sourceOffset + stride * i;

                    staticVertices[i].Normal = new Vector3(buffer.ReadSingle(), buffer.ReadSingle(), buffer.ReadSingle());
                }

                // Texture coordinates
                if (primitiveJson["attributes"]["TEXCOORD_0"] != null) {
                    int attributeAccessorId = (int)primitiveJson["attributes"]["TEXCOORD_0"];

                    if (primitiveJson["attributes"]["TEXCOORD_1"] != null)
                        throw new Exception("Dual textures primitives are not supported");

                    accessor = _json["accessors"][attributeAccessorId];
                    if ((GLTFConst)(int)accessor["componentType"] != GLTFConst.FLOAT) {
                        throw new Exception($"Found unexpected component type {(int)accessor["componentType"]} for TEXCOORD_0 attribute of {meshId}, FLOAT was expected.");
                    }

                    bufferView = _json["bufferViews"][(int)accessor["bufferView"]];
                    accessorByteOffset = (accessor["byteOffset"] != null) ? (int)accessor["byteOffset"] : 0;
                    bufferViewByteOffset = (bufferView["byteOffset"] != null) ? (int)bufferView["byteOffset"] : 0;
                    sourceOffset = bufferViewByteOffset + accessorByteOffset;
                    stride = (bufferView["byteStride"] != null) ? (int)bufferView["byteStride"] : sizeof(float) * 2;

                    buffer = _buffers[(int)bufferView["buffer"]];

                    for (var i = 0; i < verticesCount; i++) {
                        buffer.BaseStream.Position = sourceOffset + stride * i;

                        staticVertices[i].TextureCoord = new Vector2(buffer.ReadSingle(), buffer.ReadSingle());
                    }
                }

                // Indices
                ushort[] indices = new ushort[0];
                if (primitiveJson["indices"] != null) {
                    int indicesAccessorId = (int)primitiveJson["indices"];
                    var indicesAccessor = _json["accessors"][indicesAccessorId];

                    indices = new ushort[(int)indicesAccessor["count"]];
                    accessor = indicesAccessor;

                    bufferView = _json["bufferViews"][(int)accessor["bufferView"]];
                    accessorByteOffset = (accessor["byteOffset"] != null) ? (int)accessor["byteOffset"] : 0;
                    bufferViewByteOffset = (bufferView["byteOffset"] != null) ? (int)bufferView["byteOffset"] : 0;
                    sourceOffset = bufferViewByteOffset + accessorByteOffset;

                    buffer = _buffers[(int)bufferView["buffer"]];

                    for (var i = 0; i < indices.Length; i++) {
                        buffer.BaseStream.Position = sourceOffset + sizeof(ushort) * i;
                        indices[i] = buffer.ReadUInt16();
                    }
                }

                Mesh.Primitive primitive = new Mesh.Primitive(staticVertices.ToList(), indices.ToList());
                primitive.MaterialModelId = materialId;
                primitives.Add(primitive);
            }

            Mesh mesh = new Mesh(primitives.ToArray());

            if (nodeJson["matrix"] != null) {
                JArray m = (JArray)nodeJson["matrix"];
                Matrix4 initialMatrix = new Matrix4((float)m[0], (float)m[1], (float)m[2], (float)m[3], (float)m[4], (float)m[5], (float)m[6], (float)m[7], (float)m[8], (float)m[9], (float)m[10], (float)m[11], (float)m[12], (float)m[13], (float)m[14], (float)m[15]);
                mesh.LocalMatrix = initialMatrix;
            }

            if (nodeJson["name"] != null) {
                mesh.Name = (string)nodeJson["name"];
            }

            return mesh;
        }

        private string getTextureResourcePath(int textureId)
        {
            var textureJson = _json["textures"][textureId];

            int imageId = (int)textureJson["source"];
            var imageJson = _json["images"][imageId];
            return _resourcePath + "." + (string)imageJson["uri"];
        }

        enum GLTFConst
        {
            BYTE = 5120,
            UNSIGNED_BYTE = 5121,
            SHORT = 5122,
            UNSIGNED_SHORT = 5123,
            UNSIGNED_INT = 5125,
            FLOAT = 5126
        }

        enum GLTFPrimitiveMode
        {
            POINTS = 0,
            LINES = 1,
            LINE_LOOP = 2,
            LINE_STRIP = 3,
            TRIANGLES = 4,
            TRIANGLE_STRIP = 5,
            TRIANGLE_FAN = 6
        }
    }
}
