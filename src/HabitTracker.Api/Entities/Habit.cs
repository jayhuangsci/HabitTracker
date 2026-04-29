public class Habit
{
    int ID;
    string Name;
    string Unit;
    string? Description;
    DateTime CreatAt;
    ICollection<HabitLog> HabitLogs;

}