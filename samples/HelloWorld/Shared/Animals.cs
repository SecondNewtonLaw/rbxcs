using RobloxCS;

namespace Demo;

[Shared]
public class Animal {
    public string Name;

    public Animal(string name) {
        Name = name;
    }

    public virtual string Speak() {
        return "...";
    }

    public string Describe() {
        return Name;
    }
}

[Shared]
public class Dog : Animal {
    public Dog(string name) : base(name) { }

    public override string Speak() {
        return "Woof";
    }
}