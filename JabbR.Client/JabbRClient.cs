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
        private int _initialized;
        private int _connectInitialzed;

        public JabbRClient(string url)
        {
            _url = url;
            _connection = new HubConnection(url);
            _chat = _connection.CreateProxy("JabbR.Chat");
        }

        public event Action<Message, string> MessageReceived;
        public event Action<IEnumerable<string>> LoggedOut;
        public event Action<User, string> UserJoined;
        public event Action<User, string> UserLeft;
        public event Action<string> Kicked;
        public event Action<string, string, string> PrivateMessage;
        public event Action<User, string> UserTyping;

        // Global
        public event Action<string, int> RoomCountChanged;
        public event Action<User> UserActivityChanged;
        public event Action<IEnumerable<User>> UsersInactive;

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

            return DoConnect(() => _connection.Start()
                                              .Then(() => _chat.Invoke<bool>("Join"))
                                              .FastUnwrap());
        }

        public Task<LogOnInfo> Connect(string name, string password)
        {
            return DoConnect(() => _connection.Start()
                                              .Then(() =>
                                              {
                                                  return _chat.Invoke<bool>("Join").Then(success =>
                                                  {
                                                      if (!success)
                                                      {
                                                          return SendCommand("nick {0} {1}", name, password);
                                                      }
                                                      return TaskAsyncHelper.Empty;
                                                  }).FastUnwrap();

                                              }).FastUnwrap());
        }

        private Task<LogOnInfo> DoConnect(Func<Task> connect)
        {
            SubscribeToEvents();

            var tcs = new TaskCompletionSource<LogOnInfo>();

            if (Interlocked.Exchange(ref _connectInitialzed, 1) == 0)
            {
                _chat.On<IEnumerable<Room>>(ClientEvents.LogOn, rooms =>
                {
                    tcs.SetResult(new LogOnInfo
                    {
                        Rooms = rooms,
                        UserId = (string)_chat["id"]
                    });
                });

                _chat.On(ClientEvents.UserCreated, () =>
                {
                    tcs.SetResult(new LogOnInfo
                    {
                        UserId = (string)_chat["id"]
                    });
                });
            }

            connect().ContinueWith(task =>
            {
                if (task.IsCanceled)
                {
                    tcs.SetCanceled();
                }
                else if (task.IsFaulted)
                {
                    tcs.SetException(task.Exception);
                }
            },
            TaskContinuationOptions.NotOnRanToCompletion);

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

        public Task<bool> Send(string message, string room)
        {
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
                    ExecuteWithSyncContext(() => messageReceived(message, room));
                });
            }

            Action<IEnumerable<string>> loggedOut = LoggedOut;

            if (loggedOut != null)
            {
                _chat.On<IEnumerable<string>>(ClientEvents.LogOut, rooms =>
                {
                    ExecuteWithSyncContext(() => loggedOut(rooms));
                });
            }

            Action<User, string> userJoined = UserJoined;

            if (userJoined != null)
            {
                _chat.On<User, string>(ClientEvents.AddUser, (user, room) =>
                {
                    ExecuteWithSyncContext(() => userJoined(user, room));
                });
            }

            Action<User, string> userLeft = UserLeft;

            if (userLeft != null)
            {
                _chat.On<User, string>(ClientEvents.Leave, (user, room) =>
                {
                    ExecuteWithSyncContext(() => userLeft(user, room));
                });
            }

            Action<string> kicked = Kicked;

            if (kicked != null)
            {
                _chat.On<string>(ClientEvents.Kick, room =>
                {
                    ExecuteWithSyncContext(() => kicked(room));
                });
            }

            Action<string, int> roomCountChanged = RoomCountChanged;

            if (roomCountChanged != null)
            {
                _chat.On<string, int>(ClientEvents.UpdateRoomCount, (room, count) =>
                {
                    ExecuteWithSyncContext(() => roomCountChanged(room, count));
                });
            }

            Action<User> userActivityChanged = UserActivityChanged;

            if (userActivityChanged != null)
            {
                _chat.On<User>(ClientEvents.UpdateActivity, user =>
                {
                    ExecuteWithSyncContext(() => userActivityChanged(user));
                });
            }

            Action<string, string, string> privateMessage = PrivateMessage;

            if (privateMessage != null)
            {
                _chat.On<string, string, string>(ClientEvents.SendPrivateMessage, (from, to, message) =>
                {
                    ExecuteWithSyncContext(() => privateMessage(from, to, message));
                });
            }

            Action<IEnumerable<User>> usersInactive = UsersInactive;

            if (usersInactive != null)
            {
                _chat.On<IEnumerable<User>>(ClientEvents.MarkInactive, (users) =>
                {
                    ExecuteWithSyncContext(() => usersInactive(users));
                });
            }

            Action<User, string> userTyping = UserTyping;

            if (userTyping != null)
            {
                _chat.On<User, string>(ClientEvents.SetTyping, (user, room) =>
                {
                    ExecuteWithSyncContext(() => userTyping(user, room));
                });
            }
        }

        private static void ExecuteWithSyncContext(Action action)
        {
            // Store the current sync context so we call the user code 
            // where it was meant to be raised
            var syncContext = SynchronizationContext.Current;
            Task.Factory.StartNew(() =>
            {
                if (syncContext != null)
                {
                    syncContext.Post(_ => action(), null);
                }
                else
                {
                    action();
                }
            })
            .Catch();
        }

        private Task SendCommand(string command, params object[] args)
        {
            return _chat.Invoke("Send", String.Format("/" + command, args), null);
        }
    }
}
