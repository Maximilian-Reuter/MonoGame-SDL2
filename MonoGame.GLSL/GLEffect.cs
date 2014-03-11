/*
 * Copyright (c) 2013-2014 Tobias Schulz, Maximilian Reuter, Pascal Knodel,
 *                         Gerd Augsburg, Christina Erler, Daniel Warzel
 *
 * This source code file is part of Knot3. Copying, redistribution and
 * use of the source code in this file in source and binary forms,
 * with or without modification, are permitted provided that the conditions
 * of the MIT license are met:
 *
 *   Permission is hereby granted, free of charge, to any person obtaining a copy
 *   of this software and associated documentation files (the "Software"), to deal
 *   in the Software without restriction, including without limitation the rights
 *   to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
 *   copies of the Software, and to permit persons to whom the Software is
 *   furnished to do so, subject to the following conditions:
 *
 *   The above copyright notice and this permission notice shall be included in all
 *   copies or substantial portions of the Software.
 *
 *   THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 *   IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 *   FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 *   AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 *   LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
 *   OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
 *   SOFTWARE.
 *
 * See the LICENSE file for full license details of the Knot3 project.
 */
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using OpenTK.Graphics.OpenGL;

namespace MonoGame.GLSL
{
    public class GLEffect : IEffectMatrices
    {
        private GraphicsDevice GraphicsDevice;

        private List<GLShaderProgram> Shaders;

        public Matrix Projection { get; set; }

        public Matrix View { get; set; }

        public Matrix World { get; set; }

        public GLParamaterCollection Parameters { get; private set; }
        
        private GLEffect (GraphicsDevice graphicsDevice, IEnumerable<GLShaderProgram> shaderPrograms)
        {
            GraphicsDevice = graphicsDevice;
            Shaders = shaderPrograms.ToList ();
            Parameters = new GLParamaterCollection ();
        }

        public static GLEffect FromFiles (GraphicsDevice graphicsDevice, string pixelShaderFilename, string vertexShaderFilename)
        {
            GLShader pixelShader = new GLShader (graphicsDevice, ShaderStage.Pixel, File.ReadAllText (pixelShaderFilename));
            GLShader vertexShader = new GLShader (graphicsDevice, ShaderStage.Vertex, File.ReadAllText (vertexShaderFilename));
            GLShaderProgram shaderProgram = new GLShaderProgram (vertex: vertexShader, pixel: pixelShader);
            return new GLEffect (graphicsDevice: graphicsDevice, shaderPrograms: new GLShaderProgram[] { shaderProgram });
        }

        public void Draw (Model model)
        {
            int boneCount = model.Bones.Count;

            // Look up combined bone matrices for the entire model.
            Matrix[] sharedDrawBoneMatrices = new Matrix [boneCount];
            model.CopyAbsoluteBoneTransformsTo (sharedDrawBoneMatrices);

            // Draw the model.
            foreach (ModelMesh mesh in model.Meshes) {
                Matrix world = sharedDrawBoneMatrices [mesh.ParentBone.Index] * World;
                Draw (mesh, world);
            }
        }

        public void Draw (ModelMesh mesh)
        {
            Draw (mesh, World);
        }

        public void Draw (ModelMesh mesh, Matrix world)
        {
            foreach (ModelMeshPart part in mesh.MeshParts) {
                if (part.PrimitiveCount > 0) {
                    GraphicsDevice.SetVertexBuffer (part.VertexBuffer);
                    GraphicsDevice.Indices = part.IndexBuffer;
                    GraphicsDevice.VertexShader = Shaders [0].VertexShader;
                    GraphicsDevice.PixelShader = Shaders [0].PixelShader;
                    //part.Effect.CurrentTechnique.Passes [0].Apply ();
                    //GraphicsDevice.DrawIndexedPrimitives (PrimitiveType.TriangleList, part.VertexOffset, 0, part.NumVertices, part.StartIndex, part.PrimitiveCount);
                    DrawIndexedPrimitives (PrimitiveType.TriangleList, part.VertexOffset, 0, part.NumVertices, part.StartIndex, part.PrimitiveCount);
                }
            }
        }

        /// <summary>
        /// Draw geometry by indexing into the vertex buffer.
        /// </summary>
        /// <param name="primitiveType">The type of primitives in the index buffer.</param>
        /// <param name="baseVertex">Used to offset the vertex range indexed from the vertex buffer.</param>
        /// <param name="minVertexIndex">A hint of the lowest vertex indexed relative to baseVertex.</param>
        /// <param name="numVertices">An hint of the maximum vertex indexed.</param>
        /// <param name="startIndex">The index within the index buffer to start drawing from.</param>
        /// <param name="primitiveCount">The number of primitives to render from the index buffer.</param>
        /// <remarks>Note that minVertexIndex and numVertices are unused in MonoGame and will be ignored.</remarks>
        public void DrawIndexedPrimitives (
            PrimitiveType primitiveType,
            int baseVertex,
            int minVertexIndex,
            int numVertices,
            int startIndex,
            int primitiveCount
        )
        {
            foreach (GLShaderProgram pass in Shaders) {
                pass.Apply (parameters: Parameters);

                // Unsigned short or unsigned int?
                bool shortIndices = GraphicsDevice.Indices.IndexElementSize == IndexElementSize.SixteenBits;

                // Set up the vertex buffers
                foreach (VertexBufferBinding vertBuffer in GraphicsDevice.vertexBufferBindings) {
                    if (vertBuffer.VertexBuffer != null) {
                        OpenGLDevice.Instance.BindVertexBuffer (vertBuffer.VertexBuffer.Handle);
                        vertBuffer.VertexBuffer.VertexDeclaration.Apply (
                            pass.VertexShader,
                            (IntPtr)(vertBuffer.VertexBuffer.VertexDeclaration.VertexStride * (vertBuffer.VertexOffset + baseVertex))
                        );
                    }
                }

                // Enable the appropriate vertex attributes.
                OpenGLDevice.Instance.FlushGLVertexAttributes ();

                // Bind the index buffer
                OpenGLDevice.Instance.BindIndexBuffer (GraphicsDevice.Indices.Handle);

                // Draw!
                GL.DrawRangeElements (
                    PrimitiveTypeGL (primitiveType),
                    minVertexIndex,
                    minVertexIndex + numVertices,
                    GetElementCountArray (primitiveType, primitiveCount),
                    shortIndices ? DrawElementsType.UnsignedShort : DrawElementsType.UnsignedInt,
                    (IntPtr)(startIndex * (shortIndices ? 2 : 4))
                );

                // Check for errors in the debug context
                GraphicsExtensions.CheckGLError ();
            }
        }

        #region Private XNA->GL Conversion Methods

        private static int GetElementCountArray(PrimitiveType primitiveType, int primitiveCount)
        {
            switch (primitiveType)
            {
                case PrimitiveType.LineList:
                return primitiveCount * 2;
                case PrimitiveType.LineStrip:
                return primitiveCount + 1;
                case PrimitiveType.TriangleList:
                return primitiveCount * 3;
                case PrimitiveType.TriangleStrip:
                return 3 + (primitiveCount - 1);
            }

            throw new NotSupportedException();
        }

        private static BeginMode PrimitiveTypeGL(PrimitiveType primitiveType)
        {
            switch (primitiveType)
            {
                case PrimitiveType.LineList:
                return BeginMode.Lines;
                case PrimitiveType.LineStrip:
                return BeginMode.LineStrip;
                case PrimitiveType.TriangleList:
                return BeginMode.Triangles;
                case PrimitiveType.TriangleStrip:
                return BeginMode.TriangleStrip;
            }

            throw new ArgumentException();
        }

        #endregion
    }
}
