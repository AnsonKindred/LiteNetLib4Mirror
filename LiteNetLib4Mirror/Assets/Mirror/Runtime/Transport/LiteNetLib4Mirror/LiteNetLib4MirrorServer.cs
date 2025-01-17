using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using LiteNetLib;

namespace Mirror.LiteNetLib4Mirror
{
	public static class LiteNetLib4MirrorServer
	{
		public static readonly Dictionary<int, NetPeer> Peers = new Dictionary<int, NetPeer>();
		public static string Code { get; internal set; }

		internal static bool IsActive()
		{
			return LiteNetLib4MirrorCore.State == LiteNetLib4MirrorCore.States.Server;
		}

		internal static void StartServer(string code)
		{
			try
			{
				Code = code;
				EventBasedNetListener listener = new EventBasedNetListener();
				LiteNetLib4MirrorCore.Host = new NetManager(listener);
				listener.ConnectionRequestEvent += OnConnectionRequest;
				listener.PeerDisconnectedEvent += OnPeerDisconnected;
				listener.NetworkErrorEvent += OnNetworkError;
				listener.NetworkReceiveEvent += OnNetworkReceive;
				listener.PeerConnectedEvent += OnPeerConnected;
				if (LiteNetLib4MirrorDiscovery.Singleton != null)
				{
					listener.NetworkReceiveUnconnectedEvent += LiteNetLib4MirrorDiscovery.OnDiscoveryRequest;
				}

				LiteNetLib4MirrorCore.SetOptions(true);
				if (LiteNetLib4MirrorTransport.Singleton.useUpnP)
				{
					LiteNetLib4MirrorUtils.ForwardPort();
				}
#if DISABLE_IPV6
				LiteNetLib4MirrorCore.Host.Start(LiteNetLib4MirrorUtils.Parse(LiteNetLib4MirrorTransport.Singleton.serverIPv4BindAddress), LiteNetLib4MirrorUtils.Parse("::"), LiteNetLib4MirrorTransport.Singleton.port);
#else
				LiteNetLib4MirrorCore.Host.Start(LiteNetLib4MirrorUtils.Parse(LiteNetLib4MirrorTransport.Singleton.serverIPv4BindAddress), LiteNetLib4MirrorUtils.Parse(LiteNetLib4MirrorTransport.Singleton.serverIPv6BindAddress), LiteNetLib4MirrorTransport.Singleton.port);
#endif
				LiteNetLib4MirrorTransport.Polling = true;
				LiteNetLib4MirrorCore.State = LiteNetLib4MirrorCore.States.Server;
			}
			catch (Exception ex)
			{
				LiteNetLib4MirrorCore.State = LiteNetLib4MirrorCore.States.Idle;
				LiteNetLib4MirrorUtils.LogException(ex);
			}
		}

		private static void OnPeerConnected(NetPeer peer)
		{
			Peers.Add(peer.Id + 1, peer);
			LiteNetLib4MirrorTransport.Singleton.OnServerConnected.Invoke(peer.Id + 1);
		}

		private static void OnNetworkReceive(NetPeer peer, NetPacketReader reader, DeliveryMethod deliverymethod)
		{
#if NONALLOC_RECEIVE
			LiteNetLib4MirrorTransport.Singleton.OnServerDataReceivedNonAlloc.Invoke(peer.Id + 1, reader.GetRemainingBytesSegment());
#else
			LiteNetLib4MirrorTransport.Singleton.OnServerDataReceived.Invoke(peer.Id + 1, reader.GetRemainingBytes());
#endif
			reader.Recycle();
		}

		private static void OnNetworkError(IPEndPoint endpoint, SocketError socketerror)
		{
			LiteNetLib4MirrorCore.LastError = socketerror;
			for (NetPeer peer = LiteNetLib4MirrorCore.Host.FirstPeer; peer != null; peer = peer.NextPeer)
			{
				if (peer.EndPoint.ToString() == endpoint.ToString())
				{
					LiteNetLib4MirrorTransport.Singleton.OnServerError.Invoke(peer.Id + 1, new SocketException((int)socketerror));
					LiteNetLib4MirrorTransport.Singleton.onServerSocketError.Invoke(peer.Id + 1, socketerror);
					return;
				}
			}
		}

		private static void OnPeerDisconnected(NetPeer peer, DisconnectInfo disconnectinfo)
		{
			LiteNetLib4MirrorCore.LastDisconnectError = disconnectinfo.SocketErrorCode;
			LiteNetLib4MirrorCore.LastDisconnectReason = disconnectinfo.Reason;
			LiteNetLib4MirrorTransport.Singleton.OnServerDisconnected.Invoke(peer.Id + 1);
			Peers.Remove(peer.Id + 1);
		}

		private static void OnConnectionRequest(ConnectionRequest request)
		{
			LiteNetLib4MirrorTransport.Singleton.ProcessConnectionRequest(request);
		}

		internal static bool Send(int connectionId, DeliveryMethod method, byte[] data, int start, int length, byte channelNumber)
		{
			try
			{
				Peers[connectionId].Send(data, start, length, channelNumber, method);
				return true;
			}
			catch
			{
				return false;
			}
		}

		internal static bool Disconnect(int connectionId)
		{
			try
			{
				Peers[connectionId].Disconnect();
				return true;
			}
			catch
			{
				return false;
			}
		}

		internal static string GetClientAddress(int connectionId)
		{
			return Peers[connectionId].EndPoint.Address.ToString();
		}
	}
}
