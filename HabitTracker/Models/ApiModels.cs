using System;
using System.Collections.Generic;
using System.Text;

namespace HabitTracker.Models
{
    public class HabitDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Unit { get; set; } = string.Empty;
        public string? Description { get; set; }
        public DateTime CreatedAt { get; set; }
    }
    public class CreateHabitRequest
    {
        public string Name { get; set; } = string.Empty;
        public string Unit { get; set; } = string.Empty;
        public string? Description { get; set; }
    }

    public class UpdateHabitRequest
    {
        public string Name { get; set; } = string.Empty;
        public string Unit { get; set; } = string.Empty;
        public string? Description { get; set; }
    }

    public class HabitLogDto
    {
        public int Id { get; set; }
        public int HabitId { get; set; }
        public DateOnly Date { get; set; }
        public decimal Quantity { get; set; }
        public string? Comment { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class CreateHabitLogRequest
    {
        public int HabitId { get; set; }
        public DateOnly Date { get; set; }
        public decimal Quantity { get; set; }
        public string? Comment { get; set; }
    }

    public class UpdateHabitLogRequest
    {
        public int HabitId { get; set; }
        public DateOnly Date { get; set; }
        public decimal Quantity { get; set; }
        public string? Comment { get; set; }
    }

    public class StatPointDto
    {
        public DateOnly Date { get; set; }
        public decimal Quantity { get; set; }
    }

    public class HabitStatsDto
    {
        public int HabitId { get; set; }
        public string HabitName { get; set; } = string.Empty;
        public string Unit { get; set; } = string.Empty;
        public string Range { get; set; } = string.Empty;
        public DateOnly StartDate { get; set; }
        public DateOnly EndDate { get; set; }
        public List<StatPointDto> Points { get; set; } = [];
    }
}
