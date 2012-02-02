using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using JabbR.Client.Models;
using SignalR.Client.Hubs;

namespace JabbR.Client
{
    public class JabbRClient
    {
        private readonly IHubProxy _chat;
        private readonly HubConnection _connection;
        private readonly string _url;

        public JabbRClient(string url)
        {
            _url = url;
            _connection = new HubConnection(url);
            _chat = _connection.CreateProxy("JabbR.Chat");
        }

        public event Action<Message, string> MessageReceived;

        public ICredentials Credentials
        {
            get
            {
                return _connection.Credentials;
            }
            set
            {
                _connection.Credentials = value;
            }
        }

        public event Action Disconnected
        {
            add
            {
                _connection.Closed += value;
            }
            remove
            {
                _connection.Closed -= value;
            }
        }

        public string UserId
        {
            get
            {
                return (string)_chat["id"];
            }
            set
            {
                _chat["id"] = value;
            }
        }

        public Task Connect(string name, string password)
        {
            SubscribeToEvents();

            return _connection.Start()
                              .Then(() =>
                              {
                                  return _chat.Invoke<bool>("Join")
                                              .Then(success =>
                                              {
                                                  if (!success)
                                                  {
                                                      return SendCommand("nick {0} {1}", name, password);
                                                  }

                                                  return TaskAsyncHelper.Empty;
                                              })
                                              .FastUnwrap();
                              })
                              .FastUnwrap();
        }

        private void SubscribeToEvents()
        {
            Action<Message, string> messageReceived = MessageReceived;

            if (messageReceived != null)
            {
                _chat.On<Message, string>(ClientEvents.AddMessage, (message, room) =>
                {
                    // Store the current sync context so we call the user code 
                    // where it was meant to be raised
                    var syncContext = SynchronizationContext.Current;
                    Task.Factory.StartNew(() =>
                    {
                        if (syncContext != null)
                        {
                            syncContext.Post(_ => messageReceived(message, room), null);
                        }
                        else
                        {
                            messageReceived(message, room);
                        }
                    })
                    .Catch();
                });
            }
        }

        public Task<User> GetUserInfo()
        {
            return _chat.Invoke<User>("GetUserInfo");
        }

        public Task LogOut()
        {
            return _chat.Invoke("LogOut");
        }

        public Task<bool> Send(string message, string room)
        {
            if (message.StartsWith("/"))
            {
                throw new InvalidOperationException("Invoking commands not allowed. Use one of the methods to invoke commands.");
            }

            return _chat.Invoke<bool>("Send", message, room);
        }

        public Task JoinRoom(string room)
        {
            return SendCommand("join {0}", room);
        }

        public Task LeaveRoom(string room)
        {
            return SendCommand("leave {0}", room);
        }

        public Task SetFlag(string countryCode)
        {
            return SendCommand("flag {0}", countryCode);
        }

        public Task SetNote(string noteText)
        {
            return SendCommand("note {0}", noteText);
        }

        public Task SendPrivateMessage(string userName, string message)
        {
            return SendCommand("msg {0} {1}", userName, message);
        }

        public Task Kick(string userName, string room)
        {
            return SendCommand("kick {0} {1}", userName, room);
        }

        public Task ChangeName(string oldName, string newName)
        {
            return SendCommand("nick {0}", newName);
        }

        public Task<bool> CheckStatus()
        {
            return _chat.Invoke<bool>("CheckStatus");
        }

        public Task SetTyping(string room)
        {
            return _chat.Invoke("Typing", room);
        }

        public Task<IEnumerable<Message>> GetPreviousMessages(string fromId)
        {
            return _chat.Invoke<IEnumerable<Message>>("GetPreviousMessages", fromId);
        }

        public Task<Room> GetRoomInfo(string roomName)
        {
            return _chat.Invoke<Room>("GetRoomInfo", roomName);
        }

        public Task<IEnumerable<Room>> GetRooms()
        {
            return _chat.Invoke<IEnumerable<Room>>("GetRooms");
        }

        public void Disconnect()
        {
            _connection.Stop();
        }

        private Task SendCommand(string command, params object[] args)
        {
            return _chat.Invoke("Send", String.Format("/" + command, args), null);
        }
    }
}
