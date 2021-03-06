﻿using System;
using System.Threading;
using System.Threading.Tasks;
using Lime.Protocol.Network;
using Lime.Protocol.Security;

namespace Lime.Protocol.Server
{
    /// <summary>
    /// Defines the communication channel
    /// between a server and a node
    /// </summary>
    public class ServerChannel : ChannelBase, IServerChannel
    {
        #region Constructor

        /// <summary>
        /// Initializes a new instance of the <a href="ServerChannel"/> class.
        /// </summary>
        /// <param name="sessionId"></param>
        /// <param name="serverNode"></param>
        /// <param name="transport"></param>
        /// <param name="sendTimeout"></param>
        /// <param name="buffersLimit"></param>
        /// <param name="fillEnvelopeRecipients"></param>
        /// <param name="autoReplyPings">Indicates if the channel should reply automatically to ping request commands. In this case, the ping command are not returned by the ReceiveCommandAsync method.</param>
        public ServerChannel(Guid sessionId, Node serverNode, ITransport transport, TimeSpan sendTimeout, int buffersLimit = 5, bool fillEnvelopeRecipients = false, bool autoReplyPings = false)
            : base(transport, sendTimeout, buffersLimit, fillEnvelopeRecipients, autoReplyPings)
        {
            LocalNode = serverNode;
            SessionId = sessionId;
        }

        #endregion

        #region IServerChannel Members

        /// <summary>
        /// Receives a new session envelope
        /// from the client node.
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        /// <exception cref="System.InvalidOperationException">
        /// Cannot await for a session response since there's already a listener.
        /// </exception>
        public async Task<Session> ReceiveNewSessionAsync(CancellationToken cancellationToken)
        {
            if (State != SessionState.New)
            {
                throw new InvalidOperationException(string.Format("Cannot receive a new session in the '{0}' state", State));                
            }

            return await ReceiveSessionAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Changes the session state and 
        /// sends a negotiate session envelope
        /// to the node with the available 
        /// options and awaits for the client
        /// selected option.
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <param name="compressionOptions">The session compression options.</param>
        /// <param name="encryptionOptions"></param>
        /// <returns>
        /// A negotiating session envelope with the client node selected options.
        /// </returns>
        /// <exception cref="System.ArgumentNullException">
        /// compressionOptions
        /// or
        /// encryptionOptions
        /// </exception>
        /// <exception cref="System.ArgumentException">
        /// No available options for compression negotiation
        /// or
        /// No available options for encryption negotiation
        /// </exception>
        /// <exception cref="System.InvalidOperationException">Cannot await for a session response since there's already a listener.</exception>
        public async Task<Session> NegotiateSessionAsync(SessionCompression[] compressionOptions, SessionEncryption[] encryptionOptions, CancellationToken cancellationToken)
        {
            if (State != SessionState.New)
            {
                throw new InvalidOperationException(string.Format("Cannot start a session negotiating in the '{0}' state", State));
            }

            if (compressionOptions == null)
            {
                throw new ArgumentNullException("compressionOptions");
            }

            if (compressionOptions.Length == 0)
            {
                throw new ArgumentException("No available options for compression negotiation");
            }

            if (encryptionOptions == null)
            {
                throw new ArgumentNullException("encryptionOptions");
            }

            if (encryptionOptions.Length == 0)
            {
                throw new ArgumentException("No available options for encryption negotiation");
            }

            State = SessionState.Negotiating;

            var session = new Session
            {
                Id = SessionId,
                From = LocalNode,
                State = State,
                CompressionOptions = compressionOptions,
                EncryptionOptions = encryptionOptions
            };

            await SendSessionAsync(session).ConfigureAwait(false);
            return await ReceiveSessionAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Send a negotiate session envelope
        /// to the remote node to confirm the 
        /// session negotiation options.
        /// </summary>
        /// <param name="sessionCompression">The session compression option</param>
        /// <param name="sessionEncryption">The session encryption option</param>
        /// <returns></returns>
        /// <exception cref="System.InvalidOperationException"></exception>
        public Task SendNegotiatingSessionAsync(SessionCompression sessionCompression, SessionEncryption sessionEncryption)
        {
            if (State != SessionState.Negotiating)
            {
                throw new InvalidOperationException(string.Format("Cannot negotiate a session in the '{0}' state", State));
            }

            var session = new Session
            {
                Id = SessionId,
                From = LocalNode,
                State = State,
                Compression = sessionCompression,
                Encryption = sessionEncryption
            };

            return SendSessionAsync(session);
        }

        /// <summary>
        /// Changes the session state and 
        /// sends an authenticat envelope
        /// to the node with the available options 
        /// and awaits for the client authentication.
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <param name="schemeOptions"></param>
        /// <returns>
        /// A autheticating session envelope with the authentication information.
        /// </returns>
        /// <exception cref="System.ArgumentNullException">authentication</exception>
        /// <exception cref="System.ArgumentException">No available options for authentication</exception>
        /// <exception cref="System.InvalidOperationException">Cannot await for a session response since there's already a listener.</exception>
        public async Task<Session> AuthenticateSessionAsync(AuthenticationScheme[] schemeOptions, CancellationToken cancellationToken)
        {
            if (State != SessionState.New &&
                State != SessionState.Negotiating)
            {
                throw new InvalidOperationException(string.Format("Cannot start the session authentication in the '{0}' state", State));
            }

            if (schemeOptions == null)
            {
                throw new ArgumentNullException("authentication");
            }

            if (schemeOptions.Length == 0)
            {
                throw new ArgumentException("No available options for authentication");
            }

            State = SessionState.Authenticating;

            var session = new Session
            {
                Id = SessionId,
                From = LocalNode,
                State = State,
                SchemeOptions = schemeOptions
            };

            await SendSessionAsync(session).ConfigureAwait(false);
            return await ReceiveSessionAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Sends authentication roundtrip information
        /// to the connected node and awaits
        /// for the client authentication.
        /// </summary>
        /// <param name="authenticationRoundtrip">The authentication roundtrip data.</param>
        /// <returns>
        /// A autheticating session envelope with the authentication information.
        /// </returns>
        /// <exception cref="System.ArgumentNullException">authenticationRoundtrip</exception>
        /// <exception cref="System.InvalidOperationException"></exception>
        public async Task<Session> AuthenticateSessionAsync(Authentication authenticationRoundtrip, CancellationToken cancellationToken)
        {
            if (authenticationRoundtrip == null)
            {
                throw new ArgumentNullException("authenticationRoundtrip");
            }

            if (State != SessionState.Authenticating)
            {
                throw new InvalidOperationException(string.Format("Cannot send an authentication roundtrip for a session in the '{0}' state", State));
            }

            var session = new Session
            {
                Id = SessionId,
                From = LocalNode,
                State = State,
                Authentication = authenticationRoundtrip
            };

            await SendSessionAsync(session).ConfigureAwait(false);
            return await ReceiveSessionAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Changes the session state and 
        /// sends a finished session envelope
        /// to the node to comunicate the
        /// end of the session
        /// </summary>
        /// <param name="node"></param>
        /// <returns></returns>
        /// <exception cref="System.ArgumentNullException">node</exception>
        public Task SendEstablishedSessionAsync(Node node)
        {
            if (node == null)
            {
                throw new ArgumentNullException("node");
            }

            if (State > SessionState.Authenticating)
            {
                throw new InvalidOperationException(string.Format("Cannot establish a session in the '{0}' state", State));
            }

            State = SessionState.Established;
            RemoteNode = node;           

            var session = new Session
            {
                Id = SessionId,
                From = LocalNode,
                To = RemoteNode,
                State = State
            };

            return SendSessionAsync(session);
        }

        /// <summary>
        /// Receives a finishing session envelope
        /// from the client node.
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        /// <exception cref="System.InvalidOperationException">
        /// Cannot await for a session response since there's already a listener.
        /// </exception>
        public async Task<Session> ReceiveFinishingSessionAsync(CancellationToken cancellationToken)
        {
            if (State != SessionState.Established)
            {
                throw new InvalidOperationException(string.Format("Cannot receive a new session in the '{0}' state", State));
            }

            return await ReceiveSessionAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Changes the session state and 
        /// sends a finished session envelope
        /// to the node to comunicate the
        /// end of the session and closes
        /// the transport
        /// </summary>
        /// <returns></returns>
        /// <exception cref="System.InvalidOperationException"></exception>
        public async Task SendFinishedSessionAsync()
        {            
            var session = new Session
            {
                Id = SessionId,
                From = LocalNode,
                To = RemoteNode,
                State = SessionState.Finished
            };

            await SendSessionAsync(session).ConfigureAwait(false);
            await Transport.CloseAsync(CancellationToken.None).ConfigureAwait(false);
            State = session.State;
        }

        /// <summary>
        /// Changes the session state and 
        /// sends a failed session envelope
        /// to the node to comunicate the
        /// finished session and closes
        /// the transport
        /// </summary>
        /// <param name="reason"></param>
        /// <returns></returns>
        /// <exception cref="System.ArgumentNullException">reason</exception>
        public async Task SendFailedSessionAsync(Reason reason)
        {
            if (reason == null)
            {
                throw new ArgumentNullException("reason");
            }            

            var session = new Session
            {
                Id = SessionId,
                From = LocalNode,
                To = RemoteNode,
                State = SessionState.Failed,
                Reason = reason
            };

            await SendSessionAsync(session).ConfigureAwait(false);
            await Transport.CloseAsync(CancellationToken.None).ConfigureAwait(false);
            State = session.State;
        }

        /// <summary>
        /// Receives a session
        /// from the remote node.
        /// Avoid to use this method directly. Instead,
        /// use the Server or Client channel methods.
        /// </summary>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns></returns>
        public override async Task<Session> ReceiveSessionAsync(CancellationToken cancellationToken)
        {
            var session = await base.ReceiveSessionAsync(cancellationToken).ConfigureAwait(false);

            if (session.State != SessionState.New && 
                session.Id != SessionId)
            {
                await SendFailedSessionAsync(new Reason
                {
                    Code = ReasonCodes.SESSION_ERROR,
                    Description = "Invalid session id"
                });
            }

            return session;
        }

        #endregion
    }
}
