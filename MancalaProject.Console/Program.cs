using System;
using System.Linq;
using System.Threading;

namespace MancalaProject
{
    /// <summary>
    /// Console entry point and presentation layer for the Mancala game.
    /// Connects user input and screen rendering to the rule engine and the computer agent.
    /// </summary>
    class Program
    {
        /// <summary>
        /// Pause (in milliseconds) between announcing "Computer is thinking..."
        /// and actually playing the move, so the human user can perceive the action.
        /// </summary>
        private const int ComputerMoveDelayMs = 2500;

        /// <summary>
        /// Application entry point. Configures console encoding for board characters
        /// and starts the main game loop.
        /// </summary>
        /// <param name="args">Command-line arguments (unused).</param>
        static void Main(string[] args)
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            RunGame();
        }

        static void PrintBoard(GameEngine engine)
        {
            var board = engine.Board;
            Console.WriteLine("\n      [06] [05] [04] [03] [02] [01]   << Player 2");
            Console.WriteLine($"       {board[12],2}   {board[11],2}   {board[10],2}   {board[9],2}   {board[8],2}   {board[7],2}");
            Console.WriteLine($" [{board[13],2}]                              [{board[6],2}]");
            Console.WriteLine($"       {board[0],2}   {board[1],2}   {board[2],2}   {board[3],2}   {board[4],2}   {board[5],2}");
            Console.WriteLine("      [01] [02] [03] [04] [05] [06]   << Player 1");
            Console.WriteLine($"\n  P2 Store = {engine.GetScore(Player.Player2),2}              P1 Store = {engine.GetScore(Player.Player1),2}");
            Console.WriteLine("─────────────────────────────────────────");
        }

        static void RunGame()
        {
            Console.WriteLine("Choose difficulty:");
            Console.WriteLine("  1. Easy");
            Console.WriteLine("  2. Medium");
            Console.WriteLine("  3. Hard");
            Console.Write("Enter 1-3: ");
            string? diffInput = Console.ReadLine();
            Difficulty diff = diffInput?.Trim() switch
            {
                "1" => Difficulty.Easy,
                "2" => Difficulty.Medium,
                _ => Difficulty.Hard
            };
            GreedyAgent agent = new GreedyAgent(Player.Player2, diff);

            GameEngine engine = new GameEngine();
            string statusMessage = "";

            while (!engine.IsGameOver())
            {
                RenderTurn(engine, statusMessage);
                statusMessage = "";

                bool isComputerTurn = engine.CurrentPlayer == Player.Player2;
                statusMessage = isComputerTurn
                    ? RunComputerTurn(engine, agent)
                    : RunHumanTurn(engine);
            }

            engine.FinalizeBoard();
            Console.Clear();
            PrintBoard(engine);
            Console.WriteLine("GAME OVER!");

            int winner = engine.GetWinner();
            if (winner == 0) Console.WriteLine("Result: Player 1 Wins!");
            else if (winner == 1) Console.WriteLine("Result: Computer Wins!");
            else Console.WriteLine("Result: It's a Tie!");

            Console.WriteLine("\nPress Enter to exit...");
            Console.ReadLine();
        }

        static void RenderTurn(GameEngine engine, string statusMessage)
        {
            Console.Clear();
            PrintBoard(engine);
            if (!string.IsNullOrEmpty(statusMessage))
                Console.WriteLine(statusMessage);
        }

        static string RunComputerTurn(GameEngine engine, GreedyAgent agent)
        {
            Console.WriteLine("\nComputer is thinking...");
            Thread.Sleep(ComputerMoveDelayMs);
            int move = agent.CalculateMove(engine);
            MoveResult result = agent.ExecuteMove(engine, move);

            int displayPit = move - GameEngine.Player1Store; // 1..6
            string captureText = result.CaptureOccurred
                ? $" Captured {result.CapturedStoneCount} stones!"
                : "";
            string extraTurnText = result.ExtraTurn ? " Computer gets an extra turn!" : "";
            return $">> Computer played pit {displayPit}.{captureText}{extraTurnText}";
        }

        static string RunHumanTurn(GameEngine engine)
        {
            const string playerName = "Player 1";
            Console.WriteLine($"\n{playerName}'s turn.");
            ShowValidPits(engine);

            Console.Write($"Enter pit number (1-{GameEngine.PitsPerPlayer}): ");
            string? input = Console.ReadLine();
            return TryHumanMove(engine, playerName, input);
        }

        static void ShowValidPits(GameEngine engine)
        {
            var displayMoves = engine.GetValidMoves().Select(m =>
                engine.CurrentPlayer == Player.Player1 ? m + 1 : m - GameEngine.Player1Store);
            Console.WriteLine($"Valid pits: {string.Join(", ", displayMoves)}");
        }

        static string TryHumanMove(GameEngine engine, string playerName, string? input)
        {
            bool parsed = int.TryParse(input, out int pitNumber)
                && pitNumber >= 1 && pitNumber <= GameEngine.PitsPerPlayer;
            if (!parsed) return $">> Invalid input! Enter a number 1-{GameEngine.PitsPerPlayer}.";

            int pitIndex = engine.GetPitIndex(engine.CurrentPlayer, pitNumber - 1);
            if (!engine.IsValidMove(pitIndex)) return ">> That pit is empty!";

            return ApplyHumanMove(engine, playerName, pitIndex);
        }

        static string ApplyHumanMove(GameEngine engine, string playerName, int pitIndex)
        {
            MoveResult result = engine.ApplyMove(pitIndex);
            string captureText = result.CaptureOccurred
                ? $">> {playerName} captured {result.CapturedStoneCount} stones!\n"
                : "";
            string extraTurnText = result.ExtraTurn
                ? $">> {playerName} gets an extra turn!"
                : "";
            return captureText + extraTurnText;
        }
    }
}
