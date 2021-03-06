﻿/*
 * Apache License, Version 2.0
 * Copyright 2019-2020 NVIDIA Corporation
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *     http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

using Ardalis.GuardClauses;
using Nvidia.Clara.DicomAdapter.API.Rest;
using System.Net.Http.Headers;

namespace Nvidia.Clara.DicomAdapter.Server.Common
{
    public static class AuthenticationHeaderValueExtensions
    {
        public static AuthenticationHeaderValue ConvertFrom(ConnectionAuthType connectionAuthType, string authId)
        {
            Guard.Against.NullOrWhiteSpace(authId, nameof(authId));
            switch (connectionAuthType)
            {
                case ConnectionAuthType.Basic:
                    return new AuthenticationHeaderValue("Basic", authId);

                case ConnectionAuthType.Bearer:
                    return new AuthenticationHeaderValue("Bearer", authId);

                case ConnectionAuthType.None:
                    return null;

                default:
                    throw new InferenceRequestException($"Unsupported ConnectionAuthType: {connectionAuthType}");
            }
        }
    }
}