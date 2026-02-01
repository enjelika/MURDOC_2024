using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace MURDOC_2024.Services
{
    public class PerformanceMetricsService
    {
        private Stopwatch _sessionTimer;
        private Stopwatch _taskTimer;
        private DateTime _sessionStart;
        private string _currentImagePath;

        private List<InteractionEvent> _interactions;
        private List<TaskMetrics> _tasks;

        public PerformanceMetricsService()
        {
            _interactions = new List<InteractionEvent>();
            _tasks = new List<TaskMetrics>();
            _sessionTimer = new Stopwatch();
            _taskTimer = new Stopwatch();
        }

        #region Session Management

        public void StartSession()
        {
            _sessionStart = DateTime.Now;
            _sessionTimer.Restart();

            LogInteraction("SessionStart", null);
        }

        public void EndSession()
        {
            _sessionTimer.Stop();
            LogInteraction("SessionEnd", new
            {
                Duration = _sessionTimer.Elapsed.TotalSeconds
            });
        }

        public TimeSpan GetSessionDuration()
        {
            return _sessionTimer.Elapsed;
        }

        #endregion

        #region Task Tracking

        public void StartTask(string imagePath)
        {
            // End previous task if running
            if (_taskTimer.IsRunning)
            {
                EndTask();
            }

            _currentImagePath = imagePath;
            _taskTimer.Restart();

            LogInteraction("TaskStart", new { ImagePath = imagePath });
        }

        public void EndTask()
        {
            if (!_taskTimer.IsRunning)
                return;

            _taskTimer.Stop();

            var taskMetrics = new TaskMetrics
            {
                ImagePath = _currentImagePath,
                StartTime = DateTime.Now.AddSeconds(-_taskTimer.Elapsed.TotalSeconds),
                Duration = _taskTimer.Elapsed.TotalSeconds,
                InteractionCount = _interactions.Count(i =>
                    i.Timestamp >= DateTime.Now.AddSeconds(-_taskTimer.Elapsed.TotalSeconds))
            };

            _tasks.Add(taskMetrics);

            LogInteraction("TaskEnd", new
            {
                ImagePath = _currentImagePath,
                Duration = _taskTimer.Elapsed.TotalSeconds
            });
        }

        #endregion

        #region Interaction Logging

        public void LogInteraction(string eventType, object data = null)
        {
            var interaction = new InteractionEvent
            {
                Timestamp = DateTime.Now,
                SessionTime = _sessionTimer.Elapsed.TotalSeconds,
                TaskTime = _taskTimer.IsRunning ? _taskTimer.Elapsed.TotalSeconds : 0,
                EventType = eventType,
                ImagePath = _currentImagePath,
                Data = data
            };

            _interactions.Add(interaction);
        }

        // Specific interaction types
        public void LogImageLoad(string imagePath, double loadTime)
        {
            LogInteraction("ImageLoad", new { ImagePath = imagePath, LoadTime = loadTime });
        }

        public void LogModelRun(double executionTime)
        {
            LogInteraction("ModelExecution", new { ExecutionTime = executionTime });
        }

        public void LogDetectionClick(string detectionId, string label, double confidence)
        {
            LogInteraction("DetectionClick", new
            {
                DetectionId = detectionId,
                Label = label,
                Confidence = confidence
            });
        }

        public void LogFeedback(string feedbackType, string detectionId)
        {
            LogInteraction("Feedback", new
            {
                Type = feedbackType,
                DetectionId = detectionId
            });
        }

        public void LogROIDrawing(string mode, int pointCount)
        {
            LogInteraction("ROIDrawing", new { Mode = mode, PointCount = pointCount });
        }

        public void LogParameterChange(string parameter, object oldValue, object newValue)
        {
            LogInteraction("ParameterChange", new
            {
                Parameter = parameter,
                OldValue = oldValue,
                NewValue = newValue
            });
        }

        #endregion

        #region Metrics Calculation

        public PerformanceReport GenerateReport()
        {
            var report = new PerformanceReport
            {
                SessionStart = _sessionStart,
                SessionDuration = _sessionTimer.Elapsed.TotalSeconds,
                TotalTasks = _tasks.Count,
                TotalInteractions = _interactions.Count,

                // Task metrics
                AverageTaskDuration = _tasks.Any() ? _tasks.Average(t => t.Duration) : 0,
                MinTaskDuration = _tasks.Any() ? _tasks.Min(t => t.Duration) : 0,
                MaxTaskDuration = _tasks.Any() ? _tasks.Max(t => t.Duration) : 0,

                // Interaction metrics
                InteractionsPerTask = _tasks.Any() ?
                    (double)_interactions.Count / _tasks.Count : 0,

                // Event counts
                ModelExecutions = _interactions.Count(i => i.EventType == "ModelExecution"),
                DetectionClicks = _interactions.Count(i => i.EventType == "DetectionClick"),
                FeedbackActions = _interactions.Count(i => i.EventType == "Feedback"),
                ROIDrawings = _interactions.Count(i => i.EventType == "ROIDrawing"),
                ParameterChanges = _interactions.Count(i => i.EventType == "ParameterChange"),

                // Response times
                AverageModelExecutionTime = CalculateAverageExecutionTime(),
                AverageTimeToFirstInteraction = CalculateAverageTimeToFirstInteraction()
            };

            return report;
        }

        private double CalculateAverageExecutionTime()
        {
            var executions = _interactions
                .Where(i => i.EventType == "ModelExecution")
                .ToList();

            if (!executions.Any())
                return 0;

            var sum = 0.0;
            var count = 0;

            foreach (var exec in executions)
            {
                if (exec.Data is Dictionary<string, object> dict &&
                    dict.ContainsKey("ExecutionTime"))
                {
                    sum += Convert.ToDouble(dict["ExecutionTime"]);
                    count++;
                }
            }

            return count > 0 ? sum / count : 0;
        }

        private double CalculateAverageTimeToFirstInteraction()
        {
            var timeToFirst = new List<double>();

            foreach (var task in _tasks)
            {
                var firstInteraction = _interactions
                    .Where(i => i.Timestamp >= task.StartTime &&
                               (i.EventType == "DetectionClick" || i.EventType == "Feedback"))
                    .OrderBy(i => i.Timestamp)
                    .FirstOrDefault();

                if (firstInteraction != null)
                {
                    var timeToFirstClick = (firstInteraction.Timestamp - task.StartTime).TotalSeconds;
                    timeToFirst.Add(timeToFirstClick);
                }
            }

            return timeToFirst.Any() ? timeToFirst.Average() : 0;
        }

        #endregion

        #region Export

        public void ExportMetrics(string filePath)
        {
            var export = new
            {
                Report = GenerateReport(),
                Tasks = _tasks,
                Interactions = _interactions
            };

            var json = JsonConvert.SerializeObject(export, Formatting.Indented);
            File.WriteAllText(filePath, json);
        }

        public void ExportCSV(string filePath)
        {
            using (var writer = new StreamWriter(filePath))
            {
                // Write header
                writer.WriteLine("Timestamp,SessionTime,TaskTime,EventType,ImagePath,Data");

                // Write interactions
                foreach (var interaction in _interactions)
                {
                    var dataStr = interaction.Data != null ?
                        JsonConvert.SerializeObject(interaction.Data) : "";

                    writer.WriteLine($"{interaction.Timestamp:yyyy-MM-dd HH:mm:ss.fff}," +
                                   $"{interaction.SessionTime:F3}," +
                                   $"{interaction.TaskTime:F3}," +
                                   $"{interaction.EventType}," +
                                   $"\"{interaction.ImagePath}\"," +
                                   $"\"{dataStr.Replace("\"", "\"\"")}\"");
                }
            }
        }

        #endregion
    }

    #region Supporting Classes

    public class InteractionEvent
    {
        public DateTime Timestamp { get; set; }
        public double SessionTime { get; set; }
        public double TaskTime { get; set; }
        public string EventType { get; set; }
        public string ImagePath { get; set; }
        public object Data { get; set; }
    }

    public class TaskMetrics
    {
        public string ImagePath { get; set; }
        public DateTime StartTime { get; set; }
        public double Duration { get; set; }
        public int InteractionCount { get; set; }
    }

    public class PerformanceReport
    {
        public DateTime SessionStart { get; set; }
        public double SessionDuration { get; set; }
        public int TotalTasks { get; set; }
        public int TotalInteractions { get; set; }

        // Task metrics
        public double AverageTaskDuration { get; set; }
        public double MinTaskDuration { get; set; }
        public double MaxTaskDuration { get; set; }

        // Interaction metrics
        public double InteractionsPerTask { get; set; }

        // Event counts
        public int ModelExecutions { get; set; }
        public int DetectionClicks { get; set; }
        public int FeedbackActions { get; set; }
        public int ROIDrawings { get; set; }
        public int ParameterChanges { get; set; }

        // Response times
        public double AverageModelExecutionTime { get; set; }
        public double AverageTimeToFirstInteraction { get; set; }
    }

    #endregion
}