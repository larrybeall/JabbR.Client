using System;
using System.Diagnostics;
using JabbR.Client.Models;

namespace JabbR.Client.Sample
{
    class Program
    {
        static void Main(string[] args)
        {
            string server = "http://jabbr-staging.apphb.com/";
            string roomName = "clienttest";
            string userName = "testclient";
            string password = "password";

            var client = new JabbRClient("http://jabbr-staging.apphb.com/");

            // Launch the test room
            Process.Start(String.Format("{0}#/rooms/{1}", server, roomName));

            // Subscribe to new messages
            client.MessageReceived += (message, room) =>
            {
                Console.WriteLine("[{0}] {1}: {2}", message.When, message.User.Name, message.Content);
            };

            client.UserJoined += (user, room) =>
            {
                Console.WriteLine("{0} joined {1}", user.Name, room);
            };

            client.UserLeft += (user, room) =>
            {
                Console.WriteLine("{0} left {1}", user.Name, room);
            };

            client.PrivateMessage += (from, to, message) =>
            {
                Console.WriteLine("*PRIVATE* {0} -> {1} ", from, message);
            };

            // Connect to chat
            var info = client.Connect(userName, password).Result;

            Console.WriteLine("Logged on successfully. You are currently in the following rooms:");
            foreach (var room in info.Rooms)
            {
                Console.WriteLine(room.Name);
                Console.WriteLine(room.Private);
            }

            Console.WriteLine("User id is {0}. Don't share this!", info.UserId);

            // Get my user info
            User myInfo = client.GetUserInfo().Result;

            Console.WriteLine(myInfo.Name);
            Console.WriteLine(myInfo.LastActivity);
            Console.WriteLine(myInfo.Status);
            Console.WriteLine(myInfo.Country);

            // Join a room called test
            client.JoinRoom(roomName).Wait();

            // Get info about the test room
            Room roomInfo = client.GetRoomInfo(roomName).Result;
            foreach (var u in roomInfo.Users)
            {
                if (u.Name != userName)
                {
                    client.SendPrivateMessage(u.Name, "hey there, this is private right?").Wait();
                }
            }

            // Set the flag
            client.SetFlag("bb").Wait();

            // Set the user note
            client.SetNote("This is testing a note").Wait();

            // Mark the client as typing
            client.SetTyping(roomName).Wait();

            // Clear the note
            client.SetNote(null).Wait();

            // Say hello to the room
            client.Send("Hello world", roomName).Wait();

            Console.WriteLine("Press any key to leave the room and disconnect");
            Console.Read();
            client.LeaveRoom(roomName).Wait();
            client.Disconnect();
        }
    }
}
