using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Xml.Linq;

class Program
{
   // Username for the chat session
static string username;

// WebSocket client instance
static ClientWebSocket ws;

// Flag to control reconnection attempts
static bool reconnect = true;

// Tasks for receiving and sending messages
static Task receiveTask = null;
static Task sendTask = null;

static async Task Main(string[] args)
{
    // Handle Ctrl+C to exit gracefully
    Console.CancelKeyPress += (sender, e) =>
    { 
        e.Cancel = true; // Prevent abrupt exit
        reconnect = false;
        Environment.Exit(0);
    };

    // Cleanup WebSocket on application exit
    AppDomain.CurrentDomain.ProcessExit += (sender, e) =>
    {
        CloseWebSocket(ws).Wait();
    };

    await ShowMainMenu(); // Start main menu
}


    private static async Task PromptUserNameAndConnect()
    {
        // Clears the console before prompting the user for input
        Console.Clear();

        // Loop to prompt the user for a valid username
        while (true)
        {
            // Check if the username is null or empty
            if (string.IsNullOrEmpty(username))
            {
                // Set the console color to cyan to prompt for input
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.Write("Input UserName: ");
                Console.ForegroundColor = ConsoleColor.White;

                // Read the user's input for the username
                username = Console.ReadLine();

                // If the input is not empty or whitespace, break out of the loop
                if (!string.IsNullOrWhiteSpace(username))
                    break;

                // Show an error message if the username is empty
                ShowErrorMessage("Username cannot be empty. Please try again.");
            }
            else
                break;  // If the username already exists, exit the loop
        }

        // Loop to attempt reconnection if 'reconnect' is true
        while (reconnect)
        { 
            await TryConnection();
        }
    }


    private static async Task ViewConnectedUsers()
    {
        // Check if the WebSocket (ws) is not null (i.e., connection exists)
        if (ws != null)
        {
            // Convert the message "getConnectedUsers" into a byte array
            var bytes = Encoding.UTF8.GetBytes("getConnectedUsers");

            // Send the request to get connected users asynchronously
            await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
        }
        else
        {
            // If ws is null, display an error and show the main menu
            Console.Clear();
            ShowErrorMessage("Please connect first.");
            await ShowMainMenu();
        }
    }

    private static async Task ShowMainMenu()
    {
        // Welcome message displayed at the top of the menu
        string welcomeText = "Welcome to ChatApp 1.0";

        // Display Menu
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine(ConsoleBorder());  // Print border
        Console.WriteLine(ConsoleCenterAlignment(welcomeText));  // Center the welcome text
        Console.WriteLine(ConsoleBorder());  // Print another border
        Console.WriteLine("Select Option:");
        Console.WriteLine("1. Connect to Chat Server");
        Console.WriteLine("2. View Connected Users");
        Console.WriteLine("3. Exit");
        Console.WriteLine(ConsoleBorder());  // Print a final border
        Console.ForegroundColor = ConsoleColor.White;
        Console.Write("Press Enter option Number: ");

        // Read user input for the menu option
        string choice = Console.ReadLine();

        // Loop until the user selects a valid option
        while (true)
        {
            switch (choice)
            {
                case "1":
                    // If choice is "1", prompt user to connect to the server
                    await PromptUserNameAndConnect();
                    break;
                case "2":
                    // If choice is "2", view connected users
                    await ViewConnectedUsers();
                    return; // Return from the method after viewing users
                case "3":
                    // If choice is "3", exit the application
                    Environment.Exit(0); // Exit the program
                    return; // Exit the method (and the loop)
                default:
                    // Handle invalid choice input
                    ShowErrorMessage("\nInvalid choice.");
                    Console.ForegroundColor = ConsoleColor.White;
                    Console.Write("Press Enter option Number: ");
                    choice = Console.ReadLine();  // Prompt user for a valid choice
                    break; // Exit the switch and prompt again
            }
        }
    }





    private static async Task TryConnection()
    {
        // Check if WebSocket (ws) is already connected and in an open state
        if (ws != null && ws.State == WebSocketState.Open)
        {
            // If already connected, close the existing WebSocket connection 
                await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Reconnect", CancellationToken.None); 
            // Cancel or dispose of any existing receive/send tasks
            receiveTask?.Dispose();
            sendTask?.Dispose(); 
        }
        if (ws != null)
        ws.Dispose(); // Dispose of the old WebSocket
        // Attempt to establish a new WebSocket connection
        ws = new ClientWebSocket();
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.Write("Connecting to Server.");

        try
        {
            // Create a task for connecting to the WebSocket server
            var connectingTask = ws.ConnectAsync(new Uri($"ws://localhost:6000/ws?name={username}"), CancellationToken.None);

            // Show progress dots while waiting for the connection to complete
            while (!connectingTask.IsCompleted)
            {
                Console.Write(".");  // Display a dot for each second of waiting
                await Task.Delay(1000);  // Wait for 1 second before displaying the next dot
            }

            // Wait for the connection task to complete and catch any exceptions
            await connectingTask;

            // If connection is successful, change text color to green and indicate success
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("\nConnected!");
            await Task.Delay(1000);  // Pause briefly
            Console.Clear();  // Clear the console screen

            // Start tasks to receive and send messages
            receiveTask = Task.Run(() => ReceiveMessages(ws));
            sendTask = Task.Run(() => SendMessages(ws));

            // Wait for either the receive or send task to complete (whichever comes first)
            await Task.WhenAny(receiveTask, sendTask);
        }
        catch (WebSocketException wse)
        {
            // Handle WebSocket specific exceptions (e.g., connection failed)
            ShowErrorMessage($"\nFailed to connect, Please try again.");
            ws.Dispose();  // Dispose the WebSocket connection
            PromptTryReconnecting();  // Prompt user to attempt reconnection
        }
        catch (Exception ex)
        {
            // Catch any unexpected exceptions and show the error message
            ShowErrorMessage($"\nAn unexpected error occurred: {ex.Message}");
        }

        // If the WebSocket is aborted (disconnected), handle the loss of connection
        if (ws.State == WebSocketState.Aborted)
        {
            ShowErrorMessage("\nConnection lost.");
            PromptTryReconnecting();  // Prompt the user to try reconnecting
        }
    }


    private static void PromptTryReconnecting()
    {

        // Prompt the user asking if they would like to reconnect
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.Write("Would you like to reconnect? (y/n): ");


        // Read the user input, trim any leading/trailing whitespace, and convert it to lowercase
        Console.ForegroundColor = ConsoleColor.White;
        string input = Console.ReadLine()?.Trim().ToLower();

        // Set the reconnect flag based on user input
        // If the user inputs 'y', reconnect is set to true, otherwise false
        reconnect = input == "y" ? true : false;
    }




    // Ensures that the WebSocket is in an open or close-received state before closing
    private static async Task CloseWebSocket(ClientWebSocket socket)
    {
        // Check if the socket is not null
        if (socket != null)
        {
            // If the WebSocket is open or in CloseReceived state, proceed to close the connection
            if (socket.State == WebSocketState.Open || socket.State == WebSocketState.CloseReceived)
            {
                // Initiate the closing of the output side of the WebSocket connection with a normal closure status
                await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "Application exiting", CancellationToken.None);

                // Output a message indicating the application is closing
                Console.Write("\nClosing Application.");

                // Print dots every second to show progress
                for (int i = 0; i < 3; i++)
                {
                    Console.Write(".");  // Print a dot
                    Thread.Sleep(1000);  // Use synchronous sleep to delay by 1 second between dots
                }
                // WebSocket connection is closed here
            }
            // If the WebSocket is already closed, just print the closing message
            else if (socket.State == WebSocketState.Closed)
            {
                Console.Write("\nClosing Application.");

                // Print dots as a delay
                for (int i = 0; i < 3; i++)
                {
                    Console.Write(".");  // Print a dot
                    Thread.Sleep(1000);  // Use synchronous sleep to delay by 1 second between dots
                }
            }
            // If the WebSocket is in an aborted state, show an error message
            else if (socket.State == WebSocketState.Aborted)
            {
                ShowErrorMessage("Connection lost.");
            }
            // If the WebSocket is in another unexpected state, return without action
            else
            {
                return;
            }
        }
        // If the socket is null, do nothing and return
        else
        {
            return;
        }
    }


    // Private method to receive messages from the WebSocket server
    // Reads messages from the server and displays them to the console
    private static async Task ReceiveMessages(ClientWebSocket socket)
    {
        var buffer = new byte[1024]; // Buffer to store received message data
        while (true)
        {
            // Receive data asynchronously from the WebSocket
            var result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);

            // Break the loop if the server sends a close message
            if (result.MessageType == WebSocketMessageType.Close)
            {
                break;
            }

            // Convert the received byte array to a string and display it
            var message = Encoding.UTF8.GetString(buffer, 0, result.Count);

            // Apply different colors based on the message content
            if (message.Contains("(" + username + ")"))
                Console.ForegroundColor = ConsoleColor.Yellow;  // For Highlight current users messages
            else if (message.Contains("SystemDisplay(System)"))
            {
                message = message.Replace("SystemDisplay", "");  // Replace system display placeholder
                Console.ForegroundColor = ConsoleColor.Magenta; // For system messages
            }
            else
                Console.ForegroundColor = ConsoleColor.DarkGreen; // Default color for other messages

            // If the message contains the "ConnectedUserList" tag, display the list of connected users
            if (message.Contains("ConnectedUserList"))
            {
                // Clear unnecessary text and display the connected users
                message = message.Replace("(System)ConnectedUserList: ", "");
                Console.WriteLine(ConsoleBorder());
                Console.WriteLine(ConsoleCenterAlignment("Connected User List:"));
                foreach (var user in message.Split(","))
                {
                    Console.WriteLine(ConsoleCenterAlignment($"{user.Trim()}"));
                }
                Console.WriteLine(ConsoleBorder());
            }
            else
            {
                // Display regular messages
                Console.WriteLine(message);
            }

            // Reset the console color to white for the next output
            Console.ForegroundColor = ConsoleColor.White;
        }
    }


    // Captures user input from the console and sends it to the server
    private static async Task SendMessages(ClientWebSocket socket)
    {
        while (true)
        {
            // Capture user input from the console
            var message = Console.ReadLine();

            // Check if the user wants to exit (reconnect is false)
            if (!reconnect)
            {
                Console.WriteLine("Exiting due to Ctrl+C...");
                break;
            }

            // If the message is null (user presses Enter without typing anything), skip the loop iteration
            if (message == null)
                continue;

            // If the user types "exit", set reconnect to false and break the loop
            if (message.ToLower() == "exit")
            {
                reconnect = false; // Prevent further reconnections
                break;  // Exit the loop
            }
            // If the user types "showmenu", display the main menu
            else if (message.ToLower().Trim().Replace(" ", "") == "showmenu")
            {
                await ShowMainMenu(); // Show the menu
            }
            else
            {
                // Convert the message to a byte array and send it to the WebSocket server
                var bytes = Encoding.UTF8.GetBytes(message);
                await socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
            }
        }
    }



    private static string ConsoleBorder()
    {
        // Get the console width
        int consoleWidth = Console.WindowWidth;

        // Create the border dynamically based on the console width
        string border = new string('=', consoleWidth);

        return border;
    }


    private static string ConsoleCenterAlignment(string text)
    {
        // Get the console width
        int consoleWidth = Console.WindowWidth;

        // Calculate padding required to center the text
        int textPadding = (consoleWidth - text.Length) / 2;

        // Return the text with leading spaces for center alignment
        return (new string(' ', textPadding) + text);
    }


    private static void ShowErrorMessage(string message) { 
        Console.ForegroundColor= ConsoleColor.Red;
        Console.WriteLine(message);
    } 
}
