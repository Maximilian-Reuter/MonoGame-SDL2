﻿#region License
/* FNA - XNA4 Reimplementation for Desktop Platforms
 * Copyright 2009-2014 Ethan Lee and the MonoGame Team
 *
 * Released under the Microsoft Public License.
 * See LICENSE for details.
 */
#endregion

namespace Microsoft.Xna.Framework.Graphics
{
    /// <summary>
    /// Represents a render target.
    /// </summary>
    internal interface IRenderTarget
    {
        /// <summary>
        /// Gets the width of the render target in pixels
        /// </summary>
        /// <value>The width of the render target in pixels.</value>
        int Width { get; }

        /// <summary>
        /// Gets the height of the render target in pixels
        /// </summary>
        /// <value>The height of the render target in pixels.</value>
        int Height { get; }

        /// <summary>
        /// Gets the usage mode of the render target.
        /// </summary>
        /// <value>The usage mode of the render target.</value>
        RenderTargetUsage RenderTargetUsage { get; }
    }
}
