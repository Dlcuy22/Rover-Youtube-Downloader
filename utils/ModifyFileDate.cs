using System;
using System.IO;

namespace Rover.Utils
{
    /// <summary>
    /// Utility class for modifying file timestamps
    /// Provides functionality to update file modification dates
    /// </summary>
    public class ModifyFileDate
    {
        /// <summary>
        /// Updates the last write time of a file to the current date and time
        /// This is useful for "touching" files to update their modification timestamp
        /// </summary>
        /// <param name="filePath">Full path to the file to modify</param>
        /// <exception cref="FileNotFoundException">Thrown when the specified file does not exist</exception>
        /// <exception cref="UnauthorizedAccessException">Thrown when lacking permission to modify the file</exception>
        /// <exception cref="DirectoryNotFoundException">Thrown when the file's directory does not exist</exception>
        public void ModifyFile(string filePath)
        {
            // Validate that the file exists before attempting to modify it
            if (File.Exists(filePath))
            {
                // Update the file's last write time to now
                // This effectively "touches" the file without changing its content
                File.SetLastWriteTime(filePath, DateTime.Now);
            }
            else
            {
                throw new FileNotFoundException($"File not found: {filePath}");
            }
        }

        /// <summary>
        /// Updates the last write time of a file to a specific date and time
        /// </summary>
        /// <param name="filePath">Full path to the file to modify</param>
        /// <param name="newDateTime">The new date and time to set as the file's last write time</param>
        /// <exception cref="FileNotFoundException">Thrown when the specified file does not exist</exception>
        /// <exception cref="UnauthorizedAccessException">Thrown when lacking permission to modify the file</exception>
        /// <exception cref="DirectoryNotFoundException">Thrown when the file's directory does not exist</exception>
        public void ModifyFile(string filePath, DateTime newDateTime)
        {
            // Validate that the file exists before attempting to modify it
            if (File.Exists(filePath))
            {
                // Set the file's last write time to the specified date and time
                File.SetLastWriteTime(filePath, newDateTime);
            }
            else
            {
                throw new FileNotFoundException($"File not found: {filePath}");
            }
        }

        /// <summary>
        /// Updates all timestamp properties of a file (creation, last access, and last write time) to the current time
        /// </summary>
        /// <param name="filePath">Full path to the file to modify</param>
        /// <exception cref="FileNotFoundException">Thrown when the specified file does not exist</exception>
        /// <exception cref="UnauthorizedAccessException">Thrown when lacking permission to modify the file</exception>
        /// <exception cref="DirectoryNotFoundException">Thrown when the file's directory does not exist</exception>
        public void ModifyAllTimestamps(string filePath)
        {
            // Validate that the file exists before attempting to modify it
            if (File.Exists(filePath))
            {
                DateTime now = DateTime.Now;
                
                // Update all three timestamp properties to the current time
                File.SetCreationTime(filePath, now);
                File.SetLastAccessTime(filePath, now);
                File.SetLastWriteTime(filePath, now);
            }
            else
            {
                throw new FileNotFoundException($"File not found: {filePath}");
            }
        }

        /// <summary>
        /// Checks if a file exists and can be modified
        /// </summary>
        /// <param name="filePath">Full path to the file to check</param>
        /// <returns>True if the file exists and can potentially be modified, false otherwise</returns>
        public bool CanModifyFile(string filePath)
        {
            try
            {
                // Check if file exists
                if (!File.Exists(filePath))
                    return false;

                // Try to get file attributes - this will throw if we don't have access
                var attributes = File.GetAttributes(filePath);
                
                // Check if the file is read-only
                return !attributes.HasFlag(FileAttributes.ReadOnly);
            }
            catch
            {
                // If any exception occurs (access denied, etc.), return false
                return false;
            }
        }
    }
}