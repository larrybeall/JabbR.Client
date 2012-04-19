using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using JabbR.Client.Models;
using SignalR.Client.Hubs;
using SignalR.Client.Transports;

namespace JabbR.Client
{
    public class JabbRClient
    {
        private readonly IHubProxy _chat;
        private readonly HubConnection _connection;
        private readonly IClientTransport _clientTransport;
        private readonly string _url;
        private int _initialized;

        public JabbRClient(string url)
            : this(url, null)
        { }

        public JabbRClient(string url, IClientTransport transport)
        {
            _url = url;
            _connection = new HubConnection(url);
            _chat = _connection.CreateProxy("JabbR.Chat");
            _clientTransport = transport ?? new AutoTransport();
        }

        public event Action<Message, string> MessageReceived;
        public event Action<IEnumerable<string>> LoggedOut;
        public event Action<User, string> UserJoined;
        public event Action<User, string> UserLeft;
        public event Action<string> Kicked;
        public event Action<string, string, string> PrivateMessage;
        public event Action<User, string> UserTyping;
        public event Action<User, string> GravatarChanged;
        public event Action<string, string, string> MeMessageReceived;
        public event Action<string, User, string> UsernameChanged;
        public event Action<User, string> NoteChanged;
        public event Action<User, string> FlagChanged;
        public event Action<Room> TopicChanged;
        public event Action<User, string> OwnerAdded;
        public event Action<User, string> OwnerRemoved;

        // Global
        public event Action<Room, int> RoomCountChanged;
        public event Action<User> UserActivityChanged;
        public event Action<IEnumerable<User>> UsersInactive;

        public string SourceUrl
        {
            get { return _url; }
        }

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

        public Task<LogOnInfo> Connect(string userId)
        {
            _chat["id"] = userId;

            return DoConnect(() => _connection.Start(_clientTransport)
                                              .Then(() => _chat.Invoke<bool>("Join")
                                                               .Then(success =>
                                                               {
                                                                   if (!success)
                                                                   {
                                                                       throw new InvalidOperationException("Unknown user id.");
                                                                   }
                                                               })));
        }

        public Task<LogOnInfo> Connect(string name, string password)
        {
            return DoConnect(() => _connection.Start(_clientTransport)
                                              .Then(() =>
                                              {
                                                  return _chat.Invoke<bool>("Join").Then(success =>
                                                  {
                                                      if (!success)
                                                      {
                                                          return SendCommand("nick {0} {1}", name, password);
                                                      }

                                                      return TaskAsyncHelper.Empty;
                                                  });
                                              }));
        }

        private Task<LogOnInfo> DoConnect(Func<Task> connect)
        {
            SubscribeToEvents();

            var tcs = new TaskCompletionSource<LogOnInfo>();

            IDisposable logOn = null;
            IDisposable userCreated = null;

            Action<LogOnInfo> callback = logOnInfo =>
            {
                if (userCreated != null)
                {
                    userCreated.Dispose();
                }

                if (logOn != null)
                {
                    logOn.Dispose();
                }

                tcs.SetResult(logOnInfo);
            };

            logOn = _chat.On<IEnumerable<Room>>(ClientEvents.LogOn, rooms =>
            {
                callback(new LogOnInfo
                {
                    Rooms = rooms,
                    UserId = (string)_chat["id"]
                });
            });

            userCreated = _chat.On(ClientEvents.UserCreated, () =>
            {
                callback(new LogOnInfo
                {
                    UserId = (string)_chat["id"]
                });
            });

            connect().ContinueWithNotComplete(tcs);

            return tcs.Task;
        }

        public Task<User> GetUserInfo()
        {
            return _chat.Invoke<User>("GetUserInfo");
        }

        public Task LogOut()
        {
            return _chat.Invoke("LogOut");
        }

        public Task<bool> Send(string message, string roomName)
        {
            return _chat.Invoke<bool>("Send", message, roomName);
        }

        public Task JoinRoom(string roomName)
        {
            var tcs = new TaskCompletionSource<object>();

            IDisposable joinRoom = null;

            joinRoom = _chat.On<Room>(ClientEvents.JoinRoom, room =>
            {
                joinRoom.Dispose();

                tcs.SetResult(null);
            });

            SendCommand("join {0}", roomName).ContinueWithNotComplete(tcs);

            return tcs.Task;
        }

        public Task LeaveRoom(string roomName)
        {
            return SendCommand("leave {0}", roomName);
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

        public Task Kick(string userName, string roomName)
        {
            return SendCommand("kick {0} {1}", userName, roomName);
        }

        public Task ChangeName(string oldName, string newName)
        {
            return SendCommand("nick {0}", newName);
        }

        public Task<bool> CheckStatus()
        {
            return _chat.Invoke<bool>("CheckStatus");
        }

        public Task SetTyping(string roomName)
        {
            return _chat.Invoke("Typing", roomName);
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

        private void SubscribeToEvents()
        {
            if (Interlocked.Exchange(ref _initialized, 1) != 0)
            {
                return;
            }

            Action<Message, string> messageReceived = MessageReceived;

            if (messageReceived != null)
            {
                _chat.On<Message, string>(ClientEvents.AddMessage, (message, room) =>
                {
                    Execute(() => messageReceived(message, room));
                });
            }

            Action<IEnumerable<string>> loggedOut = LoggedOut;

            if (loggedOut != null)
            {
                _chat.On<IEnumerable<string>>(ClientEvents.LogOut, rooms =>
                {
                    Execute(() => loggedOut(rooms));
                });
            }

            Action<User, string> userJoined = UserJoined;

            if (userJoined != null)
            {
                _chat.On<User, string>(ClientEvents.AddUser, (user, room) =>
                {
                    Execute(() => userJoined(user, room));
                });
            }

            Action<User, string> userLeft = UserLeft;

            if (userLeft != null)
            {
                _chat.On<User, string>(ClientEvents.Leave, (user, room) =>
                {
                    Execute(() => userLeft(user, room));
                });
            }

            Action<string> kicked = Kicked;

            if (kicked != null)
            {
                _chat.On<string>(ClientEvents.Kick, room =>
                {
                    Execute(() => kicked(room));
                });
            }

            Action<Room, int> roomCountChanged = RoomCountChanged;

            if (roomCountChanged != null)
            {
                _chat.On<Room, int>(ClientEvents.UpdateRoomCount, (room, count) =>
                {
                    Execute(() => roomCountChanged(room, count));
                });
            }

            Action<User> userActivityChanged = UserActivityChanged;

            if (userActivityChanged != null)
            {
                _chat.On<User>(ClientEvents.UpdateActivity, user =>
                {
                    Execute(() => userActivityChanged(user));
                });
            }

            Action<string, string, string> privateMessage = PrivateMessage;

            if (privateMessage != null)
            {
                _chat.On<string, string, string>(ClientEvents.SendPrivateMessage, (from, to, message) =>
                {
                    Execute(() => privateMessage(from, to, message));
                });
            }

            Action<IEnumerable<User>> usersInactive = UsersInactive;

            if (usersInactive != null)
            {
                _chat.On<IEnumerable<User>>(ClientEvents.MarkInactive, (users) =>
                {
                    Execute(() => usersInactive(users));
                });
            }

            Action<User, string> userTyping = UserTyping;

            if (userTyping != null)
            {
                _chat.On<User, string>(ClientEvents.SetTyping, (user, room) =>
                {
                    Execute(() => userTyping(user, room));
                });
            }

            Action<User, string> gravatarChanged = GravatarChanged;

            if (gravatarChanged != null)
            {
                _chat.On<User, string>(ClientEvents.GravatarChanged, (user, room) =>
                {
                    Execute(() => gravatarChanged(user, room));
                });
            }

            Action<string, string, string> meMessageReceived = MeMessageReceived;

            if (meMessageReceived != null)
            {
                _chat.On<string, string, string>(ClientEvents.MeMessageReceived, (user, content, room) =>
                {
                    Execute(() => meMessageReceived(user, content, room));
                });
            }

            Action<string, User, string> usernameChanged = UsernameChanged;

            if (usernameChanged != null)
            {
                _chat.On<string, User, string>(ClientEvents.UsernameChanged, (oldUserName, user, room) =>
                {
                    Execute(() => usernameChanged(oldUserName, user, room));
                });
            }

            Action<User, string> noteChanged = NoteChanged;

            if (noteChanged != null)
            {
                _chat.On<User, string>(ClientEvents.NoteChanged, (user, room) =>
                {
                    Execute(() => noteChanged(user, room));
                });
            }

            Action<User, string> flagChanged = FlagChanged;

            if (noteChanged != null)
            {
                _chat.On<User, string>(ClientEvents.NoteChanged, (user, room) =>
                {
                    Execute(() => noteChanged(user, room));
                });
            }

            Action<Room> topicChanged = TopicChanged;

            if (topicChanged != null)
            {
                _chat.On<Room>(ClientEvents.TopicChanged, (room) =>
                {
                    Execute(() => topicChanged(room));
                });
            }

            Action<User, string> ownerAdded = OwnerAdded;

            if (ownerAdded != null)
            {
                _chat.On<User, string>(ClientEvents.OwnerAdded, (user, room) =>
                {
                    Execute(() => ownerAdded(user, room));
                });
            }

            Action<User, string> ownerRemoved = OwnerRemoved;

            if (ownerRemoved != null)
            {
                _chat.On<User, string>(ClientEvents.OwnerRemoved, (user, room) =>
                {
                    Execute(() => ownerRemoved(user, room));
                });
            }
        }

        private static void Execute(Action action)
        {
            Task.Factory.StartNew(() => action()).Catch();
        }

        private Task SendCommand(string command, params object[] args)
        {
            return _chat.Invoke("Send", String.Format("/" + command, args), null);
        }
    }
}
