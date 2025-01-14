// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

#if WINDOWS_UWP
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Windows.Networking;
using Windows.Networking.Sockets;
using Windows.Security.Cryptography.Certificates;
using MQTTnet.Channel;
using MQTTnet.Client;
using MQTTnet.Server;
using System.Runtime.InteropServices.WindowsRuntime;

namespace MQTTnet.Implementations
{
    public sealed class MqttTcpChannel : IMqttChannel
    {
        readonly MqttClientTcpOptions _options;
        readonly int _bufferSize;

        StreamSocket _socket;
        Stream _readStream;
        Stream _writeStream;

        public MqttTcpChannel(MqttClientOptions clientOptions)
        {
            _options = (MqttClientTcpOptions)clientOptions.ChannelOptions;
            _bufferSize = _options.BufferSize;
        }

        public MqttTcpChannel(StreamSocket socket, X509Certificate2 clientCertificate, MqttServerOptions serverOptions)
        {
            _socket = socket ?? throw new ArgumentNullException(nameof(socket));
            _bufferSize = serverOptions.DefaultEndpointOptions.BufferSize;

            CreateStreams();

            IsSecureConnection = socket.Information.ProtectionLevel >= SocketProtectionLevel.Tls12;
            ClientCertificate = clientCertificate;

            Endpoint = _socket.Information.RemoteAddress + ":" + _socket.Information.RemotePort;
        }

        public static Func<MqttClientTcpOptions, IEnumerable<ChainValidationResult>> CustomIgnorableServerCertificateErrorsResolver { get; set; }

        public string Endpoint { get; private set; }

        public bool IsSecureConnection { get; }

        public X509Certificate2 ClientCertificate { get; }

        public async Task ConnectAsync(CancellationToken cancellationToken)
        {
            if (_socket == null)
            {
                _socket = new StreamSocket();
                _socket.Control.NoDelay = _options.NoDelay;
                _socket.Control.KeepAlive = true;
            }

            if (_options.TlsOptions?.UseTls != true)
            {
                await _socket.ConnectAsync(new HostName(_options.Server), _options.GetPort().ToString()).AsTask().ConfigureAwait(false);
            }
            else
            {
                _socket.Control.ClientCertificate = LoadCertificate(_options);

                foreach (var ignorableChainValidationResult in ResolveIgnorableServerCertificateErrors())
                {
                    _socket.Control.IgnorableServerCertificateErrors.Add(ignorableChainValidationResult);
                }

                var socketProtectionLevel = SocketProtectionLevel.Tls12;
                if (_options.TlsOptions.SslProtocol == SslProtocols.Tls11)
                {
                    socketProtectionLevel = SocketProtectionLevel.Tls11;
                }
                else if (_options.TlsOptions.SslProtocol == SslProtocols.Tls)
                {
                    socketProtectionLevel = SocketProtectionLevel.Tls10;
                }

                await _socket.ConnectAsync(new HostName(_options.Server), _options.GetPort().ToString(), socketProtectionLevel).AsTask().ConfigureAwait(false);
            }

            Endpoint = _socket.Information.RemoteAddress + ":" + _socket.Information.RemotePort;

            CreateStreams();
        }

        public Task DisconnectAsync(CancellationToken cancellationToken)
        {
            Dispose();
            return Task.FromResult(0);
        }

        public Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            return _readStream.ReadAsync(buffer, offset, count, cancellationToken);
        }

        public Task WriteAsync(ArraySegment<byte> buffer, bool isEndOfPacket, CancellationToken cancellationToken)
        {
            // In the write method only the internal buffer will be filled. So here is no
            // async/await required. The real network transmit is done when calling the
            // Flush method.
            _writeStream.Write(buffer.Array, buffer.Offset, buffer.Count);
            return _writeStream.FlushAsync(cancellationToken);
        }

        public void Dispose()
        {
            TryDispose(_readStream, () => _readStream = null);
            TryDispose(_writeStream, () => _writeStream = null);
            TryDispose(_socket, () => _socket = null);
        }

        private static Certificate LoadCertificate(IMqttClientChannelOptions options)
        {
            var certificates = options.TlsOptions?.ClientCertificatesProvider?.GetCertificates();

            if (certificates == null || certificates.Count == 0)
            {
                return null;
            }

            if (certificates.Count > 1)
            {
                throw new NotSupportedException("Only one client certificate is supported when using 'uap10.0'.");
            }

            return new Certificate(certificates[0].Export(X509ContentType.Cert).AsBuffer());
        }

        private IEnumerable<ChainValidationResult> ResolveIgnorableServerCertificateErrors()
        {
            if (CustomIgnorableServerCertificateErrorsResolver != null)
            {
                return CustomIgnorableServerCertificateErrorsResolver(_options);
            }

            var result = new List<ChainValidationResult>();

            if (_options.TlsOptions.IgnoreCertificateRevocationErrors)
            {
                result.Add(ChainValidationResult.RevocationInformationMissing);
                //_socket.Control.IgnorableServerCertificateErrors.Add(ChainValidationResult.Revoked); Not supported.
                result.Add(ChainValidationResult.RevocationFailure);
            }

            if (_options.TlsOptions.IgnoreCertificateChainErrors)
            {
                result.Add(ChainValidationResult.IncompleteChain);
            }

            if (_options.TlsOptions.AllowUntrustedCertificates)
            {
                result.Add(ChainValidationResult.Untrusted);
            }

            return result;
        }

        private void CreateStreams()
        {
            // Attention! Do not set the buffer for the read method. This will
            // limit the internal buffer and the read operation will hang forever
            // if more data than the buffer size was received.
            _readStream = _socket.InputStream.AsStreamForRead();

            _writeStream = _socket.OutputStream.AsStreamForWrite(_bufferSize);
        }

        private static void TryDispose(IDisposable disposable, Action afterDispose)
        {
            try
            {
                disposable?.Dispose();
            }
            catch (ObjectDisposedException)
            {
            }
            catch (NullReferenceException)
            {
            }
            finally
            {
                afterDispose();
            }
        }
    }
}
#endif