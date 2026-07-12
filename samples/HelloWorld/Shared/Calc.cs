using RobloxCS;

namespace Demo;

// Exercises F3 control flow + expression breadth.
[Shared]
public class Calc {
    public int SumTo(int n) {
        var total = 0;
        for (int i = 1; i <= n; i++) {
            total += i;
        }

        return total;
    }

    public string Classify(int x) {
        if (x < 0)
            return "neg";
        else if (x == 0)
            return "zero";
        else
            return "pos";
    }

    public string Grade(int score) {
        switch (score) {
            case 0:
                return "F";
            case 1:
                return "C";
            default:
                return "A";
        }
    }

    public int CountDown(int n) {
        var steps = 0;
        while (n > 0) {
            n -= 1;
            steps++;
        }

        return steps;
    }

    public string Greet(string who) => $"hi {who}!";

    public string Pick(bool b) => b ? "yes" : "no";
}