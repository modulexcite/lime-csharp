﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Lime.Protocol;
using Lime.Protocol.Serialization;
using Lime.Protocol.Server;
using Lime.Transport.Tcp;
using Lime.Protocol.Network;

namespace Lime.Sample.Server
{
    class Program
    {
        static IDictionary<Node, IServerChannel> _nodeChannelsDictionary = new Dictionary<Node, IServerChannel>();
        static Node _serverNode = Node.Parse("server@domain.com/default");

        static void Main(string[] args)
        {
            MainAsync(args).Wait();
        }

        static async Task MainAsync(string[] args)
        {
            Console.WriteLine("Starting the server...");

            // Create and start a listener
            var listenerUri = new Uri("net.tcp://localhost:55321");
            X509Certificate2 serverCertificate = null;  // You should provide a certificate for TLS
            var serializer = new EnvelopeSerializer();  // Built-in serializer

            var transportListener = new TcpTransportListener(
                listenerUri,
                serverCertificate,
                serializer);

            // Starts listening
            await transportListener.StartAsync();
            var cts = new CancellationTokenSource();
            var listenerTask = ListenAsync(transportListener, cts.Token);

            Console.WriteLine("Server started. Press ENTER to stop.");
            Console.ReadLine();
            cts.Cancel();

            await listenerTask;
            await transportListener.StopAsync();            

            Console.WriteLine("Server stopped. Press any key to exit.");
            Console.Read();
        }

        static async Task ListenAsync(ITransportListener transportListener, CancellationToken cancellationToken)
        {
            // List of all active consumer tasks
            var consumerTasks = new List<Task>();

            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    // Awaits for a new transport connection 
                    var transport = await transportListener.AcceptTransportAsync(cancellationToken);
                    Console.WriteLine("Transport connection received.");
                    await transport.OpenAsync(null, cancellationToken);

                    // Creates a new server channel, setting the session parameters
                    var sessionId = Guid.NewGuid();
                    var sendTimeout = TimeSpan.FromSeconds(60);

                    var serverChannel = new ServerChannel(
                        sessionId,
                        _serverNode,
                        transport,
                        sendTimeout);

                    var consumerTask = ConsumeAsync(serverChannel, cancellationToken)
                        .ContinueWith(t =>
                        {
                            if (t.Exception != null)
                            {
                                Console.WriteLine("Consumer task failed: {0}", t.Exception);                                
                            }

                            consumerTasks.Remove(t);
                        });

                    consumerTasks.Add(consumerTask);
                }
            }
            catch (OperationCanceledException ex)
            {
                if (ex.CancellationToken != cancellationToken)
                {
                    throw;
                }
            }

            await Task.WhenAll(consumerTasks);
        }

        static async Task ConsumeAsync(IServerChannel serverChannel, CancellationToken cancellationToken)
        {
            try
            {
                // Establishes the session without negotiation and authentication
                await serverChannel.ReceiveNewSessionAsync(cancellationToken);
                await serverChannel.SendEstablishedSessionAsync(new Node()
                {
                    Name = serverChannel.SessionId.ToString(),
                    Domain = serverChannel.LocalNode.Domain,
                    Instance = "default"
                });

                _nodeChannelsDictionary.Add(serverChannel.RemoteNode, serverChannel);

                // Consume the channel envelopes
                var consumeMessagesTask = ConsumeMessagesAsync(serverChannel, cancellationToken).WithPassiveCancellation();
                var consumeCommandsTask = ConsumeCommandsAsync(serverChannel, cancellationToken).WithPassiveCancellation();
                var consumeNotificationsTask = ConsumeNotificationsAsync(serverChannel, cancellationToken).WithPassiveCancellation();
                // Awaits for the finishing envelope
                var finishingSessionTask = serverChannel.ReceiveFinishingSessionAsync(cancellationToken);

                // Stops the consumer when any of the tasks finishes
                await
                    Task.WhenAny(finishingSessionTask, consumeMessagesTask, consumeCommandsTask,
                        consumeNotificationsTask);
            }
            catch (OperationCanceledException ex)
            {
                if (ex.CancellationToken != cancellationToken)
                {
                    throw;
                }            
            }

            if (serverChannel.RemoteNode != null)
            {
                _nodeChannelsDictionary.Remove(serverChannel.RemoteNode);    
            }            

            await serverChannel.SendFinishedSessionAsync();
        }

        static async Task ConsumeMessagesAsync(IServerChannel serverChannel, CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                Console.ResetColor();

                var message = await serverChannel.ReceiveMessageAsync(cancellationToken);

                Console.ForegroundColor = ConsoleColor.DarkRed;

                IServerChannel destinationServerChannel;
                // Check the destination of the envelope
                if (message.To == null ||
                    message.To.Equals(_serverNode))
                {
                    // Destination is the current node
                    var notification = new Notification()
                    {
                        Id = message.Id,
                        Event = Event.Received
                    };

                    await serverChannel.SendNotificationAsync(notification);
                    Console.WriteLine("Message with id '{0}' received from '{1}': {2}", message.Id, message.From ?? serverChannel.RemoteNode, message.Content);
                }
                else if (_nodeChannelsDictionary.TryGetValue(message.To, out destinationServerChannel))
                {
                    // Destination is a node that has a session with the server
                    message.From = serverChannel.RemoteNode;
                    await destinationServerChannel.SendMessageAsync(message);
                    Console.WriteLine("Message forwarded from '{0}' to '{1}'", serverChannel.RemoteNode, destinationServerChannel.RemoteNode);
                }
                else
                {
                    // Destination not found
                    var notification = new Notification()
                    {
                        Id = message.Id,
                        Event = Event.Failed,
                        Reason = new Reason()
                        {
                            Code = ReasonCodes.ROUTING_DESTINATION_NOT_FOUND,
                            Description = "Destination not found"
                        }
                    };

                    await serverChannel.SendNotificationAsync(notification);
                    Console.WriteLine("Invalid message destination from '{0}': '{1}'", serverChannel.RemoteNode, message.To);
                }
            }
        }

        static async Task ConsumeCommandsAsync(IServerChannel serverChannel, CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                Console.ResetColor();

                var command = await serverChannel.ReceiveCommandAsync(cancellationToken);

                Console.ForegroundColor = ConsoleColor.DarkGreen;


                IServerChannel destinationServerChannel;
                // Check the destination of the envelope
                if (command.To == null ||
                    command.To.Equals(_serverNode))
                {
                    // Destination is the current node
                    var responseCommand = new Command()
                    {
                        Id = command.Id,
                        Status = CommandStatus.Failure,
                        Reason = new Reason()
                        {
                            Code = ReasonCodes.COMMAND_RESOURCE_NOT_SUPPORTED,
                            Description = "The resource is not supported"
                        }
                    };

                    await serverChannel.SendCommandAsync(responseCommand);
                    Console.WriteLine("Command with id '{0}' received from '{1}' - Method: {2} - URI: {3}", command.Id, command.From ?? serverChannel.RemoteNode, command.Method, command.Uri);
                }
                else if (_nodeChannelsDictionary.TryGetValue(command.To, out destinationServerChannel))
                {
                    // Destination is a node that has a session with the server
                    command.From = serverChannel.RemoteNode;
                    await destinationServerChannel.SendCommandAsync(command);
                    Console.WriteLine("Command forwarded from '{0}' to '{1}'", serverChannel.RemoteNode, destinationServerChannel.RemoteNode);
                }
                else
                {
                    // Destination not found
                    var responseCommand = new Command()
                    {
                        Id = command.Id,
                        Status = CommandStatus.Failure,
                        Reason = new Reason()
                        {
                            Code = ReasonCodes.ROUTING_DESTINATION_NOT_FOUND,
                            Description = "Destination not found"
                        }
                    };

                    await serverChannel.SendCommandAsync(responseCommand);
                    Console.WriteLine("Invalid command destination from '{0}': '{1}'", serverChannel.RemoteNode, command.To);
                }
            }
        }

        static async Task ConsumeNotificationsAsync(IServerChannel serverChannel, CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                Console.ResetColor();

                var notification = await serverChannel.ReceiveNotificationAsync(cancellationToken);

                Console.ForegroundColor = ConsoleColor.DarkBlue;

                IServerChannel destinationServerChannel;
                // Check the destination of the envelope
                if (notification.To == null ||
                    notification.To.Equals(_serverNode))
                {
                    Console.WriteLine("Notification with id {0} received from '{1}' - Event: {2}", notification.Id, notification.From ?? serverChannel.RemoteNode, notification.Event);
                }
                else if (_nodeChannelsDictionary.TryGetValue(notification.To, out destinationServerChannel))
                {
                    // Destination is a node that has a session with the server
                    notification.From = serverChannel.RemoteNode;
                    await destinationServerChannel.SendNotificationAsync(notification);
                }
            }
        }
    }

    public static class TaskExtensions
    {
        public static Task WithPassiveCancellation(this Task task)
        {
            return task.ContinueWith(t => t, TaskContinuationOptions.OnlyOnCanceled);            
        }
    }
}
