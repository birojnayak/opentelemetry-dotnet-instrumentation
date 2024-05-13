// Copyright The OpenTelemetry Authors
// SPDX-License-Identifier: Apache-2.0

using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using IntegrationTests.Helpers;
using static IntegrationTests.Helpers.DockerFileHelper;

namespace IntegrationTests;

[CollectionDefinition(Name)]
public class AWSCollection : ICollectionFixture<AWSFixture>
{
    public const string Name = nameof(AWSCollection);
}

public class AWSFixture : IAsyncLifetime
{
    private const int LocalHostPort = 4566;

    private static readonly string AWSLocalImage = "localstack/localstack";
    // ReadImageFrom("aws.Dockerfile");

    private IContainer? _container;

    public AWSFixture()
    {
        Port = TcpPortProvider.GetOpenPort();
    }

    public int Port { get; }

    public async Task InitializeAsync()
    {
        _container = await LaunchAWSDynamoDBContainerAsync(Port);
    }

    public async Task DisposeAsync()
    {
        if (_container != null)
        {
            await ShutdownAWSDynamoDBLocalContainerAsync(_container);
        }
    }

    private static async Task<IContainer> LaunchAWSDynamoDBContainerAsync(int port)
    {
        var containerName = string.Format("aws-localstack{0}", port);

        var containersBuilder = new ContainerBuilder()
            .WithImage(AWSLocalImage)
            .WithName(containerName)
            .WithPortBinding(LocalHostPort, LocalHostPort)
            .WithPortBinding(4510, 4559)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilPortIsAvailable(LocalHostPort));

        var container = containersBuilder.Build();
        await container.StartAsync();

        return container;
    }

    private static async Task ShutdownAWSDynamoDBLocalContainerAsync(IContainer container)
    {
        await container.DisposeAsync();
    }
}
