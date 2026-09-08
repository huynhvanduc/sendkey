Console.WriteLine("SampleTarget khởi động — F5 trong VS để debug.");

for (int i = 0; i < 100; i++)
{
    int counter = i * i;
    string label = $"iteration {i}";
    Console.WriteLine($"{label}: counter={counter}");
    Thread.Sleep(200);
}
