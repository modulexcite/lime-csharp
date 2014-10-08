﻿using System;
using Lime.Protocol;
using Lime.Protocol.UnitTests;
using Lime.Protocol.Serialization;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Threading.Tasks;
using System.Net.Sockets;
using Shouldly;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Net;
using Lime.Protocol.Server;
using Lime.Protocol.Security;
using System.Text;
using Moq;
using Lime.Protocol.Network;
using System.Collections.Generic;
using System.Security.Principal;
using System.Threading.Tasks.Dataflow;


namespace Lime.Protocol.Http.UnitTests
{
    [TestClass]
    public class HttpTransportListenerTests
    {
        #region Public properties

        public string Host { get; private set; }

        public int Port { get; private set; }

        public Mock<IHttpServer> HttpServer { get; private set; }

        public Task<HttpResponse> HttpServerResponse { get; private set; }

        public BufferBlock<HttpRequest> HttpServerRequestBuffer { get; private set; }

        public Mock<IHttpTransportProvider> HttpTransportProvider { get; private set; }
        public Mock<IPrincipal> Principal { get; private set; }

        public Mock<ITransport> Transport { get; private set; }

        public DateTimeOffset SessionExpiration { get; private set; }

        public Mock<ITransportSession> EmulatedTransport { get; private set; }

        public Mock<IHttpProcessor> Processor1 { get; private set; }

        public HashSet<string> Processor1Methods { get; private set; }

        public UriTemplate Processor1Template { get; private set; }

        public HttpRequest Processor1HttpRequest { get; private set; }

        public Mock<IHttpProcessor> Processor2 { get; private set; }

        public HashSet<string> Processor2Methods { get; private set; }

        public UriTemplate Processor2Template { get; private set; }

        public HttpTransportListener Target { get; private set; }

        public CancellationToken CancellationToken { get; private set; }

        public string User { get; private set; }

        public string Password { get; private set; }

        public string MessagesUrl { get; private set; }

        public TimeSpan SendTimeout { get; private set; }

        public Guid SessionId { get; private set; }

        public Node ClientNode { get; private set; }

        public Node ServerNode { get; private set; }

        public Reason Reason { get; private set; }

        public Message SentMessage { get; private set; }

        public Notification Notification { get; set; }

        #endregion

        [TestInitialize]
        public void Arrange()
        {
            Host = "localhost";
            Port = 8080 + DataUtil.CreateRandomInt(10000);
            CancellationToken = TimeSpan.FromSeconds(5).ToCancellationToken();
            ClientNode = DataUtil.CreateNode();
            User = ClientNode.ToIdentity().ToString();
            Password = DataUtil.CreateRandomString(20);
            MessagesUrl = "http://" + Host + ":" + Port + "/messages";

            SessionId = Guid.NewGuid();
            ServerNode = DataUtil.CreateNode();
            SendTimeout = TimeSpan.FromSeconds(5);
            Reason = DataUtil.CreateReason();

            SentMessage = new Message()
            {
                To = DataUtil.CreateNode(),
                Content = DataUtil.CreateTextContent()
            };

            HttpServer = new Mock<IHttpServer>();
            HttpServerRequestBuffer = new BufferBlock<HttpRequest>();

            var tcs = new TaskCompletionSource<HttpResponse>();
            HttpServer
                .Setup(s => s.SubmitResponseAsync(It.IsAny<HttpResponse>()))
                .Callback((HttpResponse r) => tcs.SetResult(r));
            HttpServerResponse = tcs.Task;

            HttpServer
                .Setup(s => s.AcceptRequestAsync(It.IsAny<CancellationToken>()))
                .Returns((CancellationToken cancellationToken) => HttpServerRequestBuffer.ReceiveAsync(cancellationToken));

            Transport = new Mock<ITransport>();            
            EmulatedTransport = new Mock<ITransportSession>();
            SessionExpiration = DateTimeOffset.Parse("Mon, 15 Jun 2009 20:45:30 GMT");
            EmulatedTransport.SetupGet(t => t.Expiration).Returns(() => SessionExpiration);
            Principal = new Mock<IPrincipal>();
            HttpTransportProvider = new Mock<IHttpTransportProvider>();
            HttpTransportProvider
                .Setup(h => h.GetTransport(Principal.Object, It.IsAny<bool>()))
                .Returns(() => EmulatedTransport.Object);

            Processor1Methods = new HashSet<string> { "GET" };
            Processor1Template = new UriTemplate("/" + DataUtil.CreateRandomString(10));
            Processor1 = new Mock<IHttpProcessor>();
            Processor1.SetupGet(p => p.Methods).Returns(Processor1Methods);
            Processor1.SetupGet(p => p.Template).Returns(Processor1Template);

            var uri = new Uri("http://" + Host + ":" + Port + Processor1Template);
            Processor1HttpRequest = new HttpRequest("GET", uri, Principal.Object);

            Processor2Methods = new HashSet<string> { "POST", "DELETE", "GET" };
            Processor2Template = new UriTemplate("/" + DataUtil.CreateRandomString(10) + "/{id}");
            Processor2 = new Mock<IHttpProcessor>();
            Processor2.SetupGet(p => p.Methods).Returns(Processor2Methods);
            Processor2.SetupGet(p => p.Template).Returns(Processor2Template);

            Target = new HttpTransportListener(
                Port,
                Host,
                httpServer: HttpServer.Object,
                httpTransportProvider: HttpTransportProvider.Object,
                processors: new[] { Processor1.Object, Processor2.Object });

        }

        [TestCleanup]
        public void Dispose()
        {
            if (Target != null)
            {
                Target.Dispose();
            }
        }

        [TestMethod]
        public void ListenerUris_ValidHostAndPort_GetsRegisteredUris()
        {
            // Act
            var listenerUris = Target.ListenerUris;

            // Assert
            listenerUris.ShouldNotBe(null);
            listenerUris.Length.ShouldBe(3);
            listenerUris.ShouldAllBe(u => u.Host == Host && u.Port == Port);

        }

        [TestMethod]
        public async Task StartAsync_ValidHostAndPort_ServerStarted()
        {
            // Act
            await Target.StartAsync();

            // Assert
            HttpServer.Verify(s => s.Start(), Times.Once());
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public async Task StartAsync_CallTwice_ThrowsInvalidOperationException()
        {
            // Act
            await Target.StartAsync();
            await Target.StartAsync();
        }

        [TestMethod]
        public async Task AcceptTransportAsync_NewRequest_RetunsTransport()
        {
            // Act
            await Target.StartAsync();
            HttpTransportProvider.Raise(h => h.TransportCreated += null, new TransportEventArgs(Transport.Object));
            var transport = await Target.AcceptTransportAsync(CancellationToken);

            // Assert
            transport.ShouldBe(Transport.Object);
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public async Task AcceptTransportAsync_ListenerNotStarted_ThrowsInvalidOperationException()
        {
            // Act
            var transport = await Target.AcceptTransportAsync(CancellationToken);
        }

        [TestMethod]
        public async Task StopAsync_ActiveListener_StopsListening()
        {
            // Act
            await Target.StartAsync();

            await Target.StopAsync();

            // Assert
            HttpServer.Verify(s => s.Stop(), Times.Once());
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public async Task StopAsync_ListenerNotStarted_ThrowsInvalidOperationException()
        {
            // Act
            await Target.StopAsync();
        }

        [TestMethod]
        public async Task ProcessAsync_ValidUrlAuthenticatedUser_CallProcessorAndKeepSession()
        {
            // Arrange            
            var session = new Session()
            {
                Id = Guid.NewGuid(),
                State = SessionState.Established
            };

            EmulatedTransport
                .Setup(e => e.AuthenticateAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(session);

            var processorResponse = new HttpResponse(Processor1HttpRequest.CorrelatorId, HttpStatusCode.OK);

            Processor1
                .Setup(p => p.ProcessAsync(Processor1HttpRequest, It.IsAny<UriTemplateMatch>(), It.IsAny<ITransportSession>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(processorResponse)
                .Verifiable();

            // Act
            await Target.StartAsync();
            HttpServerRequestBuffer.Post(Processor1HttpRequest);

            // Assert
            var actualResponse = await HttpServerResponse;
            Processor1.Verify();
            actualResponse.ShouldBe(processorResponse);
            EmulatedTransport.Verify(t => t.FinishAsync(It.IsAny<CancellationToken>()), Times.Never());
            actualResponse.Headers.Get(Constants.SESSION_EXPIRATION_HEADER).ShouldNotBe(null);
            var actualSessionExpiration = DateTimeOffset.Parse(actualResponse.Headers.Get(Constants.SESSION_EXPIRATION_HEADER));
            actualSessionExpiration.ShouldBe(SessionExpiration);
            HttpTransportProvider.Verify(h => h.GetTransport(Principal.Object, true), Times.Once());
        }

        [TestMethod]
        public async Task ProcessAsync_InvalidSessionAuthentication_ReturnsUnauthorized()
        {
            // Arrange
            var session = new Session()
            {
                Id = Guid.NewGuid(),
                State = SessionState.Failed,
                Reason = new Reason()
                {
                    Code = ReasonCodes.SESSION_AUTHENTICATION_FAILED,
                    Description = DataUtil.CreateRandomString(50)
                }
            };

            EmulatedTransport
                .Setup(e => e.AuthenticateAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(session);

            // Act
            await Target.StartAsync();
            HttpServerRequestBuffer.Post(Processor1HttpRequest);

            // Assert
            var actualResponse = await HttpServerResponse;
            actualResponse.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
            actualResponse.StatusDescription.ShouldBe(session.Reason.Description);
        }

        [TestMethod]
        public async Task ProcessAsync_FailedSessionEmptyReason_ReturnsServiceUnavailable()
        {
            // Arrange
            var session = new Session()
            {
                Id = Guid.NewGuid(),
                State = SessionState.Failed
            };

            EmulatedTransport
                .Setup(e => e.AuthenticateAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(session);

            // Act
            await Target.StartAsync();
            HttpServerRequestBuffer.Post(Processor1HttpRequest);

            // Assert
            var actualResponse = await HttpServerResponse;
            actualResponse.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        }

        [TestMethod]
        public async Task ProcessAsync_RequestTimedOut_ReturnsTimeout()
        {
            // Arrange
            EmulatedTransport
                .Setup(e => e.AuthenticateAsync(It.IsAny<CancellationToken>()))
                .Throws<OperationCanceledException>();

            // Act
            await Target.StartAsync();
            HttpServerRequestBuffer.Post(Processor1HttpRequest);

            // Assert
            var actualResponse = await HttpServerResponse;
            actualResponse.StatusCode.ShouldBe(HttpStatusCode.RequestTimeout);
        }

        [TestMethod]
        public async Task ProcessAsync_InvalidUrl_ReturnsNotFound()
        {
            // Arrange
            var invalidHttpRequest = new HttpRequest(
                "GET", 
                new Uri("http://" + Host + ":" + Port + "/" + DataUtil.CreateRandomString(10)), 
                Principal.Object);

            // Act
            await Target.StartAsync();
            HttpServerRequestBuffer.Post(invalidHttpRequest);

            // Assert
            var actualResponse = await HttpServerResponse;
            actualResponse.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        }

        [TestMethod]
        public async Task ProcessAsync_ProcessorRaisesException_ReturnsInternalServerError()
        {
            // Arrange            
            var session = new Session()
            {
                Id = Guid.NewGuid(),
                State = SessionState.Established
            };

            EmulatedTransport
                .Setup(e => e.AuthenticateAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(session);

            var exceptionMessage = DataUtil.CreateRandomString(100);

            Processor1
                .Setup(p => p.ProcessAsync(Processor1HttpRequest, It.IsAny<UriTemplateMatch>(), It.IsAny<ITransportSession>(), It.IsAny<CancellationToken>()))
                .Throws(new Exception(exceptionMessage))
                .Verifiable();


            // Act
            await Target.StartAsync();
            HttpServerRequestBuffer.Post(Processor1HttpRequest);

            // Assert
            var actualResponse = await HttpServerResponse;
            actualResponse.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
            actualResponse.Body.ShouldContain(exceptionMessage);

            Processor1.Verify();
        }

        [TestMethod]
        public async Task ProcessAsync_SessionCloseHeader_FinishTransport()
        {
            // Arrange            
            var session = new Session()
            {
                Id = Guid.NewGuid(),
                State = SessionState.Established
            };

            EmulatedTransport
                .Setup(e => e.AuthenticateAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(session);

            Processor1HttpRequest.Headers.Add(Constants.SESSION_HEADER, Constants.CLOSE_HEADER_VALUE);

            var processorResponse = new HttpResponse(Processor1HttpRequest.CorrelatorId, HttpStatusCode.OK);

            Processor1
                .Setup(p => p.ProcessAsync(Processor1HttpRequest, It.IsAny<UriTemplateMatch>(), It.IsAny<ITransportSession>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(processorResponse)
                .Verifiable();

            // Act
            await Target.StartAsync();
            HttpServerRequestBuffer.Post(Processor1HttpRequest);

            // Assert
            var actualResponse = await HttpServerResponse;

            Processor1.Verify();
            actualResponse.ShouldBe(processorResponse);
            EmulatedTransport.Verify(t => t.FinishAsync(It.IsAny<CancellationToken>()), Times.Once());
            HttpTransportProvider.Verify(h => h.GetTransport(Principal.Object, false), Times.Once());
        }

    }
}