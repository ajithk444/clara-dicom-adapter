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

using Dicom;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Moq.Protected;
using Nvidia.Clara.Dicom.DicomWeb.Client.API;
using Nvidia.Clara.DicomAdapter.API;
using Nvidia.Clara.DicomAdapter.API.Rest;
using Nvidia.Clara.DicomAdapter.Configuration;
using Nvidia.Clara.DicomAdapter.Server.Services.Export;
using Nvidia.Clara.DicomAdapter.Server.Services.Jobs;
using Nvidia.Clara.DicomAdapter.Test.Shared;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Nvidia.Clara.DicomAdapter.Test.Unit
{
    public class DicomWebExportServiceTest
    {
        private readonly Mock<ILoggerFactory> _loggerFactory;
        private readonly Mock<IHttpClientFactory> _httpClientFactory;
        private readonly Mock<IInferenceRequestStore> _inferenceRequestStore;
        private readonly Mock<ILogger<DicomWebExportService>> _logger;
        private readonly Mock<IPayloads> _payloadsApi;
        private readonly Mock<IResultsService> _resultsService;
        private readonly IOptions<DicomAdapterConfiguration> _configuration;
        private readonly CancellationTokenSource _cancellationTokenSource;
        private Mock<HttpMessageHandler> _handlerMock;

        public DicomWebExportServiceTest()
        {
            _loggerFactory = new Mock<ILoggerFactory>();
            _httpClientFactory = new Mock<IHttpClientFactory>();
            _inferenceRequestStore = new Mock<IInferenceRequestStore>();
            _logger = new Mock<ILogger<DicomWebExportService>>();
            _payloadsApi = new Mock<IPayloads>();
            _resultsService = new Mock<IResultsService>();
            _configuration = Options.Create(new DicomAdapterConfiguration());
            _configuration.Value.Dicom.Scu.ExportSettings.PollFrequencyMs = 10;
            _cancellationTokenSource = new CancellationTokenSource();

        }

        [Fact(DisplayName = " ExportDataBlockCallback - Returns null if inference request cannot be found")]
        public async Task ExportDataBlockCallback_ReturnsNullIfInferenceRequestCannotBeFound()
        {
            var service = new DicomWebExportService(
                _loggerFactory.Object,
                _httpClientFactory.Object,
                _inferenceRequestStore.Object,
                _logger.Object,
                _payloadsApi.Object,
                _resultsService.Object,
                _configuration);

            var tasks = ExportServiceBaseTest.GenerateTaskResponse(1);
            _resultsService.Setup(p => p.GetPendingJobs(It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<int>())).Returns(Task.FromResult(tasks));
            _resultsService.Setup(p => p.ReportSuccess(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).Returns(Task.FromResult(true));
            _resultsService.Setup(p => p.ReportFailure(It.IsAny<Guid>(), It.IsAny<bool>(), It.IsAny<CancellationToken>())).Returns(Task.FromResult(true));
            _payloadsApi.Setup(p => p.Download(It.IsAny<string>(), It.IsAny<string>()))
                .Returns(Task.FromResult(new PayloadFile
                {
                    Name = tasks.First().Uris.First(),
                    Data = InstanceGenerator.GenerateDicomData()
                }));
            _inferenceRequestStore.Setup(p => p.Get(It.IsAny<string>(), It.IsAny<string>())).Returns(Task.FromResult(default(InferenceRequest)));

            var dataflowCompleted = new ManualResetEvent(false);
            service.ReportActionStarted += (sender, args) =>
            {
                dataflowCompleted.Set();
            };

            await service.StartAsync(_cancellationTokenSource.Token);
            dataflowCompleted.WaitOne(5000);

            _resultsService.Verify(
                p => p.GetPendingJobs(
                    _configuration.Value.Dicom.Scu.ExportSettings.Agent,
                    It.IsAny<CancellationToken>(),
                    10), Times.Once());
            _payloadsApi.Verify(p => p.Download(tasks.First().PayloadId, tasks.First().Uris.First()), Times.AtLeastOnce());
            _logger.VerifyLogging($"The specified job cannot be found in the inference request store and will not be exported.", LogLevel.Error, Times.AtLeastOnce());
            _logger.VerifyLogging($"Task {tasks.First().TaskId} marked as failure and will not be retried.", LogLevel.Warning, Times.AtLeastOnce());

            await StopAndVerify(service);
        }

        [Fact(DisplayName = " ExportDataBlockCallback - Returns null if inference request doesn't include a valid DICOMweb destination")]
        public async Task ExportDataBlockCallback_ReturnsNullIfInferenceRequestContainsNoDicomWebDestination()
        {
            var service = new DicomWebExportService(
                _loggerFactory.Object,
                _httpClientFactory.Object,
                _inferenceRequestStore.Object,
                _logger.Object,
                _payloadsApi.Object,
                _resultsService.Object,
                _configuration);

            var inferenceRequest = new InferenceRequest();

            var tasks = ExportServiceBaseTest.GenerateTaskResponse(1);
            _resultsService.Setup(p => p.GetPendingJobs(It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<int>())).Returns(Task.FromResult(tasks));
            _resultsService.Setup(p => p.ReportSuccess(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).Returns(Task.FromResult(true));
            _resultsService.Setup(p => p.ReportFailure(It.IsAny<Guid>(), It.IsAny<bool>(), It.IsAny<CancellationToken>())).Returns(Task.FromResult(true));
            _payloadsApi.Setup(p => p.Download(It.IsAny<string>(), It.IsAny<string>()))
                .Returns(Task.FromResult(new PayloadFile
                {
                    Name = tasks.First().Uris.First(),
                    Data = InstanceGenerator.GenerateDicomData()
                }));
            _inferenceRequestStore.Setup(p => p.Get(It.IsAny<string>(), It.IsAny<string>())).Returns(Task.FromResult(inferenceRequest));

            var dataflowCompleted = new ManualResetEvent(false);
            service.ReportActionStarted += (sender, args) =>
            {
                dataflowCompleted.Set();
            };

            await service.StartAsync(_cancellationTokenSource.Token);
            dataflowCompleted.WaitOne(5000);

            _resultsService.Verify(p => p.GetPendingJobs(_configuration.Value.Dicom.Scu.ExportSettings.Agent, It.IsAny<CancellationToken>(), 10), Times.AtLeastOnce());
            _payloadsApi.Verify(p => p.Download(tasks.First().PayloadId, tasks.First().Uris.First()), Times.AtLeastOnce());
            _logger.VerifyLogging($"The inference request contains no `outputResources` nor any DICOMweb export destinations.", LogLevel.Error, Times.AtLeastOnce());
            _logger.VerifyLogging($"Task {tasks.First().TaskId} marked as failure and will not be retried.", LogLevel.Warning, Times.AtLeastOnce());

            await StopAndVerify(service);
        }

        [Fact(DisplayName = " ExportDataBlockCallback - Records STOW failures and report")]
        public async Task ExportDataBlockCallback_RecordsStowFailuresAndReportFailure()
        {
            var service = new DicomWebExportService(
                _loggerFactory.Object,
                _httpClientFactory.Object,
                _inferenceRequestStore.Object,
                _logger.Object,
                _payloadsApi.Object,
                _resultsService.Object,
                _configuration);

            var inferenceRequest = new InferenceRequest();
            inferenceRequest.OutputResources.Add(new RequestOutputDataResource
            {
                Interface = InputInterfaceType.DicomWeb,
                ConnectionDetails = new DicomWebConnectionDetails
                {
                    AuthId = "token",
                    AuthType = ConnectionAuthType.Bearer,
                    Uri = "http://my-dicom-web.site"
                }
            });

            var tasks = ExportServiceBaseTest.GenerateTaskResponse(1);
            _resultsService.Setup(p => p.GetPendingJobs(It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<int>())).Returns(Task.FromResult(tasks));
            _resultsService.Setup(p => p.ReportSuccess(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).Returns(Task.FromResult(true));
            _resultsService.Setup(p => p.ReportFailure(It.IsAny<Guid>(), It.IsAny<bool>(), It.IsAny<CancellationToken>())).Returns(Task.FromResult(true));
            _payloadsApi.Setup(p => p.Download(It.IsAny<string>(), It.IsAny<string>()))
                .Returns(Task.FromResult(new PayloadFile
                {
                    Name = tasks.First().Uris.First(),
                    Data = InstanceGenerator.GenerateDicomData()
                }));
            _inferenceRequestStore.Setup(p => p.Get(It.IsAny<string>(), It.IsAny<string>())).Returns(Task.FromResult(inferenceRequest));

            _handlerMock = new Mock<HttpMessageHandler>();
            _handlerMock
            .Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>())
                .Throws(new Exception("error"));

            _httpClientFactory.Setup(p => p.CreateClient(It.IsAny<string>()))
                .Returns(new HttpClient(_handlerMock.Object));

            var dataflowCompleted = new ManualResetEvent(false);
            service.ReportActionStarted += (sender, args) =>
            {
                dataflowCompleted.Set();
            };

            await service.StartAsync(_cancellationTokenSource.Token);
            dataflowCompleted.WaitOne(5000);

            _resultsService.Verify(
                p => p.GetPendingJobs(
                    _configuration.Value.Dicom.Scu.ExportSettings.Agent,
                    It.IsAny<CancellationToken>(),
                    10), Times.Once());
            _payloadsApi.Verify(p => p.Download(tasks.First().PayloadId, tasks.First().Uris.First()), Times.AtLeastOnce());

            _logger.VerifyLogging($"Exporting data to {inferenceRequest.OutputResources.First().ConnectionDetails.Uri}.", LogLevel.Debug, Times.AtLeastOnce());
            _logger.VerifyLogging($"Failed to export data to DICOMweb destination.", LogLevel.Error, Times.AtLeastOnce());
            _logger.VerifyLoggingMessageBeginsWith("Task marked as failed with failure rate=", LogLevel.Warning, Times.AtLeastOnce());

            await StopAndVerify(service);
        }

        [Theory(DisplayName = "Export completes entire dataflow and reports status based on response StatusCode")]
        [InlineData(HttpStatusCode.OK)]
        [InlineData(HttpStatusCode.Accepted)]
        [InlineData(HttpStatusCode.BadRequest)]
        public async Task CompletesDataflow(HttpStatusCode httpStatusCode)
        {
            var service = new DicomWebExportService(
                _loggerFactory.Object,
                _httpClientFactory.Object,
                _inferenceRequestStore.Object,
                _logger.Object,
                _payloadsApi.Object,
                _resultsService.Object,
                _configuration);

            var url = "http://my-dicom-web.site";
            var inferenceRequest = new InferenceRequest();
            inferenceRequest.OutputResources.Add(new RequestOutputDataResource
            {
                Interface = InputInterfaceType.DicomWeb,
                ConnectionDetails = new DicomWebConnectionDetails
                {
                    AuthId = "token",
                    AuthType = ConnectionAuthType.Bearer,
                    Uri = url
                }
            });

            var tasks = ExportServiceBaseTest.GenerateTaskResponse(1);
            _resultsService.Setup(p => p.GetPendingJobs(It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<int>())).Returns(Task.FromResult(tasks));
            _resultsService.Setup(p => p.ReportSuccess(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).Returns(Task.FromResult(true));
            _resultsService.Setup(p => p.ReportFailure(It.IsAny<Guid>(), It.IsAny<bool>(), It.IsAny<CancellationToken>())).Returns(Task.FromResult(true));
            _payloadsApi.Setup(p => p.Download(It.IsAny<string>(), It.IsAny<string>()))
                .Returns(Task.FromResult(new PayloadFile
                {
                    Name = tasks.First().Uris.First(),
                    Data = InstanceGenerator.GenerateDicomData()
                }));
            _inferenceRequestStore.Setup(p => p.Get(It.IsAny<string>(), It.IsAny<string>())).Returns(Task.FromResult(inferenceRequest));

            var response = new HttpResponseMessage(httpStatusCode);
            response.Content = new StringContent("result");

            _handlerMock = new Mock<HttpMessageHandler>();
            _handlerMock
            .Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>())
                .ReturnsAsync(response);

            _httpClientFactory.Setup(p => p.CreateClient(It.IsAny<string>()))
                .Returns(new HttpClient(_handlerMock.Object));

            var dataflowCompleted = new ManualResetEvent(false);
            service.ReportActionStarted += (sender, args) =>
                {
                    dataflowCompleted.Set();
                };

            await service.StartAsync(_cancellationTokenSource.Token);
            dataflowCompleted.WaitOne(5000);

            _resultsService.Verify(
                p => p.GetPendingJobs(
                    _configuration.Value.Dicom.Scu.ExportSettings.Agent,
                    It.IsAny<CancellationToken>(),
                    10), Times.Once());
            _payloadsApi.Verify(p => p.Download(tasks.First().PayloadId, tasks.First().Uris.First()), Times.AtLeastOnce());

            _logger.VerifyLogging($"Exporting data to {inferenceRequest.OutputResources.First().ConnectionDetails.Uri}.", LogLevel.Debug, Times.AtLeastOnce());

            if (httpStatusCode == HttpStatusCode.OK)
            {
                _logger.VerifyLogging($"Task marked as successful.", LogLevel.Information, Times.AtLeastOnce());
            }
            else
            {
                _logger.VerifyLogging($"Failed to export data to DICOMweb destination.", LogLevel.Error, Times.AtLeastOnce());
                _logger.VerifyLoggingMessageBeginsWith("Task marked as failed with failure rate=", LogLevel.Warning, Times.AtLeastOnce());
            }

            _handlerMock.Protected().Verify(
               "SendAsync",
               Times.Exactly(1),
               ItExpr.Is<HttpRequestMessage>(req =>
                req.Method == HttpMethod.Post &&
                req.RequestUri.ToString().StartsWith($"{url}/studies/")),
               ItExpr.IsAny<CancellationToken>());

            await StopAndVerify(service);
        }

        private async Task StopAndVerify(DicomWebExportService service)
        {
            await service.StopAsync(_cancellationTokenSource.Token);
            _resultsService.Invocations.Clear();
            _logger.VerifyLogging($"Export Task Watcher Hosted Service is stopping.", LogLevel.Information, Times.Once());
            Thread.Sleep(500);
            _resultsService.Verify(p => p.GetPendingJobs(TestExportService.AgentName, It.IsAny<CancellationToken>(), 10), Times.Never());
        }
    }
}