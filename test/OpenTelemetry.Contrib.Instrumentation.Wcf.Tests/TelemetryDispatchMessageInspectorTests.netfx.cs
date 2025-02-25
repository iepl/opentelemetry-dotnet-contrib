﻿// <copyright file="TelemetryDispatchMessageInspectorTests.netfx.cs" company="OpenTelemetry Authors">
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
#if NETFRAMEWORK
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.ServiceModel;
using System.ServiceModel.Channels;
using System.Threading.Tasks;
using OpenTelemetry.Trace;
using Xunit;
using Xunit.Abstractions;

namespace OpenTelemetry.Contrib.Instrumentation.Wcf.Tests
{
    [Collection("WCF")]
    public class TelemetryDispatchMessageInspectorTests : IDisposable
    {
        private readonly ITestOutputHelper output;
        private readonly Uri serviceBaseUri;
        private readonly ServiceHost serviceHost;

        public TelemetryDispatchMessageInspectorTests(ITestOutputHelper outputHelper)
        {
            this.output = outputHelper;

            Random random = new Random();
            var retryCount = 5;
            while (retryCount > 0)
            {
                try
                {
                    this.serviceBaseUri = new Uri($"net.tcp://localhost:{random.Next(2000, 5000)}/");
                    this.serviceHost = new ServiceHost(new Service(), this.serviceBaseUri);
                    var endpoint = this.serviceHost.AddServiceEndpoint(
                        typeof(IServiceContract),
                        new NetTcpBinding(),
                        "/Service");
                    endpoint.Behaviors.Add(new TelemetryEndpointBehavior());
                    this.serviceHost.Open();
                    break;
                }
                catch (Exception ex)
                {
                    this.output.WriteLine(ex.ToString());
                    if (this.serviceHost.State == CommunicationState.Faulted)
                    {
                        this.serviceHost.Abort();
                    }
                    else
                    {
                        this.serviceHost.Close();
                    }

                    this.serviceHost = null;
                    retryCount--;
                }
            }

            if (this.serviceHost == null)
            {
                throw new InvalidOperationException("ServiceHost could not be started.");
            }
        }

        public void Dispose()
        {
            this.serviceHost?.Close();
        }

        [Theory]
        [InlineData(true, false)]
        [InlineData(true, true)]
        [InlineData(false)]
        [InlineData(true, false, true)]
        [InlineData(true, false, true, true)]
        [InlineData(true, false, true, true, true)]
        [InlineData(true, false, true, true, true, true)]
        public async Task IncomingRequestInstrumentationTest(
            bool instrument,
            bool filter = false,
            bool includeVersion = false,
            bool enrich = false,
            bool enrichmentExcecption = false,
            bool emptyOrNullAction = false)
        {
            List<Activity> stoppedActivities = new List<Activity>();

            using ActivityListener activityListener = new ActivityListener
            {
                ShouldListenTo = activitySource => true,
                ActivityStopped = activity => stoppedActivities.Add(activity),
            };

            ActivitySource.AddActivityListener(activityListener);

            TracerProvider tracerProvider = null;
            if (instrument)
            {
                tracerProvider = Sdk.CreateTracerProviderBuilder()
                    .AddWcfInstrumentation(options =>
                    {
                        if (enrich)
                        {
                            if (!enrichmentExcecption)
                            {
                                options.Enrich = (activity, eventName, message) =>
                                {
                                    switch (eventName)
                                    {
                                        case WcfEnrichEventNames.AfterReceiveRequest:
                                            activity.SetTag(
                                                "server.afterreceiverequest",
                                                WcfEnrichEventNames.AfterReceiveRequest);
                                            break;
                                        case WcfEnrichEventNames.BeforeSendReply:
                                            activity.SetTag(
                                                "server.beforesendreply",
                                                WcfEnrichEventNames.BeforeSendReply);
                                            break;
                                    }
                                };
                            }
                            else
                            {
                                options.Enrich = (activity, eventName, message) =>
                                {
                                    throw new Exception("Failure whilst enriching activity");
                                };
                            }
                        }

                        options.IncomingRequestFilter = (Message m) =>
                        {
                            return !filter;
                        };
                        options.SetSoapMessageVersion = includeVersion;
                    })
                    .Build();
            }

            ServiceClient client = new ServiceClient(
                new NetTcpBinding(),
                new EndpointAddress(new Uri(this.serviceBaseUri, "/Service")));
            try
            {
                if (emptyOrNullAction)
                {
                    await client.ExecuteWithEmptyActionNameAsync(
                        new ServiceRequest
                        {
                            Payload = "Hello Open Telemetry!",
                        }).ConfigureAwait(false);
                }
                else
                {
                    await client.ExecuteAsync(
                        new ServiceRequest
                        {
                            Payload = "Hello Open Telemetry!",
                        }).ConfigureAwait(false);
                }
            }
            finally
            {
                if (client.State == CommunicationState.Faulted)
                {
                    client.Abort();
                }
                else
                {
                    client.Close();
                }

                tracerProvider?.Shutdown();
                tracerProvider?.Dispose();

                WcfInstrumentationActivitySource.Options = null;
            }

            if (instrument && !filter)
            {
                Assert.NotEmpty(stoppedActivities);
                Assert.Single(stoppedActivities);

                Activity activity = stoppedActivities[0];

                if (emptyOrNullAction)
                {
                    Assert.Equal(WcfInstrumentationActivitySource.IncomingRequestActivityName, activity.DisplayName);
                    Assert.Equal("ExecuteWithEmptyActionName", activity.TagObjects.FirstOrDefault(t => t.Key == WcfInstrumentationConstants.RpcMethodTag).Value);
                    Assert.Equal("http://opentelemetry.io/Service/ExecuteWithEmptyActionNameResponse", activity.TagObjects.FirstOrDefault(t => t.Key == WcfInstrumentationConstants.SoapReplyActionTag).Value);
                }
                else
                {
                    Assert.Equal("http://opentelemetry.io/Service/Execute", activity.DisplayName);
                    Assert.Equal("Execute", activity.TagObjects.FirstOrDefault(t => t.Key == WcfInstrumentationConstants.RpcMethodTag).Value);
                    Assert.Equal("http://opentelemetry.io/Service/ExecuteResponse", activity.TagObjects.FirstOrDefault(t => t.Key == WcfInstrumentationConstants.SoapReplyActionTag).Value);
                }

                Assert.Equal(WcfInstrumentationActivitySource.IncomingRequestActivityName, activity.OperationName);
                Assert.Equal(WcfInstrumentationConstants.WcfSystemValue, activity.TagObjects.FirstOrDefault(t => t.Key == WcfInstrumentationConstants.RpcSystemTag).Value);
                Assert.Equal("http://opentelemetry.io/Service", activity.TagObjects.FirstOrDefault(t => t.Key == WcfInstrumentationConstants.RpcServiceTag).Value);
                Assert.Equal(this.serviceBaseUri.Host, activity.TagObjects.FirstOrDefault(t => t.Key == WcfInstrumentationConstants.NetHostNameTag).Value);
                Assert.Equal(this.serviceBaseUri.Port, activity.TagObjects.FirstOrDefault(t => t.Key == WcfInstrumentationConstants.NetHostPortTag).Value);
                Assert.Equal("net.tcp", activity.TagObjects.FirstOrDefault(t => t.Key == WcfInstrumentationConstants.WcfChannelSchemeTag).Value);
                Assert.Equal("/Service", activity.TagObjects.FirstOrDefault(t => t.Key == WcfInstrumentationConstants.WcfChannelPathTag).Value);

                if (includeVersion)
                {
                    Assert.Equal("Soap12 (http://www.w3.org/2003/05/soap-envelope) Addressing10 (http://www.w3.org/2005/08/addressing)", activity.TagObjects.FirstOrDefault(t => t.Key == WcfInstrumentationConstants.SoapMessageVersionTag).Value);
                }

                if (enrich && !enrichmentExcecption)
                {
                    Assert.Equal(WcfEnrichEventNames.AfterReceiveRequest, activity.TagObjects.Single(t => t.Key == "server.afterreceiverequest").Value);
                    Assert.Equal(WcfEnrichEventNames.BeforeSendReply, activity.TagObjects.Single(t => t.Key == "server.beforesendreply").Value);
                }
            }
            else
            {
                Assert.Empty(stoppedActivities);
            }
        }
    }
}
#endif
