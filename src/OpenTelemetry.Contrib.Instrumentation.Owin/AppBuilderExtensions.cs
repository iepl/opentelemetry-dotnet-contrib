﻿// <copyright file="AppBuilderExtensions.cs" company="OpenTelemetry Authors">
// Copyright The OpenTelemetry Authors
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
// </copyright>

using System.Diagnostics;
using OpenTelemetry.Contrib.Instrumentation.Owin;

namespace Owin
{
    /// <summary>
    /// Provides extension methods for the <see cref="IAppBuilder"/> class.
    /// </summary>
    public static class AppBuilderExtensions
    {
        /// <summary>Adds a component to the OWIN pipeline for instrumenting
        /// incoming request with <see cref="Activity"/> and notifying listeners
        /// with <see cref="ActivitySource"/>.</summary>
        /// <param name="appBuilder"><see cref="IAppBuilder"/>.</param>
        /// <returns>Supplied <see cref="IAppBuilder"/> for chaining.</returns>
        public static IAppBuilder UseOpenTelemetry(this IAppBuilder appBuilder)
            => appBuilder.Use<DiagnosticsMiddleware>();
    }
}
