﻿using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using DotNetCqs.DependencyInjection;
using DotNetCqs.DependencyInjection.Microsoft;
using DotNetCqs.MessageProcessor;
using DotNetCqs.Queues.AdoNet.IntegrationTests.CompleteTest.Messages;
using DotNetCqs.Queues.AdoNet.IntegrationTests.CompleteTest.Messages.Handlers;
using DotNetCqs.Queues.AdoNet.IntegrationTests.CompleteTest.Messages.Messages;
using DotNetCqs.Queues.AdoNet.IntegrationTests.Helpers;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace DotNetCqs.Queues.AdoNet.IntegrationTests.CompleteTest
{
    public class ScenarioTest : IDisposable, IClassFixture<TestDbFixture>
    {
        TestDbFixture _fixture;
        private ServiceProvider _serviceProvider;

        public ScenarioTest(TestDbFixture fixture)
        {
            this._fixture = fixture;
            _fixture.ClearQueue("inbound");
            _fixture.ClearQueue("outbound");

        }

        [Fact]
        public async Task Should_be_able_to_handle_a_message_flow()
        {
            ManualResetEvent evt = new ManualResetEvent(false);
            var upgrade = new LogAdminUpgrades(evt);
            var serviceProvider = new ServiceCollection()
                .AddScoped<IMessageHandler<ActivateUser>, ActivateUserHandler>()
                .AddScoped<IMessageHandler<UserActivated>, UpgradeToAdminHandler>()
                .AddSingleton<IMessageHandler<UserBecameAdmin>>(upgrade)
                .AddScoped<IQueryHandler<FindUser, FindUserResult>, FindUserHandler>()
                .BuildServiceProvider();
            var scopeFactory = new MicrosoftHandlerScopeFactory(serviceProvider);
            var inboundQueue = _fixture.OpenQueue("inbound", false);
            var outboundQueue = _fixture.OpenQueue("outbound", false);
            var token = new CancellationTokenSource();
            using (var session = inboundQueue.BeginSession())
            {
                await session.EnqueueAsync(new Message(new ActivateUser()));
                await session.SaveChanges();
            }
            var listener1 = new QueueListener(inboundQueue, outboundQueue, scopeFactory);
            listener1.MessageInvokerFactory=scope => new MessageInvoker(scope);
            listener1.Logger = (level, queue, msg) => Console.WriteLine($"{level} {queue} {msg}");
            var listener2 = new QueueListener(outboundQueue, outboundQueue, scopeFactory);
            listener2.MessageInvokerFactory = scope => new MessageInvoker(scope);
            listener2.Logger = (level, queue, msg) => Console.WriteLine($"{level} {queue} {msg}");

            var t1 = listener1.RunAsync(token.Token);
            var t2 = listener2.RunAsync(token.Token);


            token.Cancel();
            await Task.WhenAll(t1, t2);
            evt.WaitOne(500).Should().BeTrue();
        }

        public void Dispose()
        {
            _fixture?.Dispose();
        }
    }
}
