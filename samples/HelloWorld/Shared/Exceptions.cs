using RobloxCS;
using System;

namespace Demo;

[Shared]
public class MyError : Exception {
    public string Info;

    public MyError(string info) {
        Info = info;
    }
}

[Shared]
public class ExcTest {
    public string Trace = "";

    public string Basic() {
        try {
            throw new MyError("boom");
        }
        catch (MyError e) {
            return "caught:" + e.Info;
        }
    }

    public string FinallyOrder() {
        try {
            Trace += "try;";
            throw new MyError("x");
        }
        catch (Exception) {
            Trace += "catch;";
        }
        finally {
            Trace += "finally;";
        }

        return Trace;
    }

    public string ReturnInTry() {
        try {
            return "ret";
        }
        finally {
            Trace += "fin;";
        }
    }

    public string Rethrow() {
        try {
            try {
                throw new MyError("inner");
            }
            catch (MyError) {
                throw;
            }
        }
        catch (MyError e) {
            return "outer:" + e.Info;
        }
    }
}