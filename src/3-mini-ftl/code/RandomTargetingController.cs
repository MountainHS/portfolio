using System.Collections.Generic;
using UnityEngine;

namespace FTL
{
    // 랜덤 방정보를 제공하는 클래스
    public class RandomTargeter : ITargeting
    {
        private readonly List<Room> rooms = new();

        #region Public Method

        public RandomTargeter(List<Room> rooms)
        {
            for (int i = 0; i < rooms.Count; i++)
            {
                this.rooms.Add(rooms[i]);
            }
        }

        public Room GetTarget()
        {
            int randomIndex = Random.Range(0, rooms.Count);
            return rooms[randomIndex];
        }

        #endregion
    }
}
