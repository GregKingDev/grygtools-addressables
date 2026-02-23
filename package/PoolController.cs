using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace GrygTools.Pooling
{
    ///Designed as a pooling system for projects using pre 2021 unity versions
    public class PoolController : MbSingleton<PoolController>
    {
        private class LeaseHandle
        {
            private bool inUse;
            public bool InUse => inUse;
            private readonly GameObject obj;
            public GameObject Obj => obj;
            private readonly GameObject template;
            private readonly IPoolableObject poolableObject;

            public LeaseHandle(GameObject template)
            {
                this.template = template;
                obj = Instantiate(template, Instance.GetLane(template));
                obj.SetActive(true);
                obj.TryGetComponent(out poolableObject);
            }
            
            public bool HasComponent(System.Type type)
            {
                if (obj != null)
                {
                    return obj.TryGetComponent(type, out Component test);
                }

                return false;
            }
            
            public bool TryLease(out GameObject leaseObject)
            {
                leaseObject = null;
                if (obj == null)
                {
                    Instance.RemoveObj(template, this);
                }
                if (inUse)
                {
                    return false;
                }
                leaseObject = obj;
                inUse = true;
                if (poolableObject != null)
                {
                    poolableObject.InitPoolable();
                }
				
                return true;
            }
            
            public void Return()
            {
                if (obj == null)
                {
                    Instance.RemoveObj(template, this);
                }
                else
                {
                    if (poolableObject != null)
                    {
                        poolableObject.ReturnPoolable();
                    }
                    
                    inUse = false;
                    obj.SetActive(false);
                    obj.transform.SetParent(Instance.GetLane(template));
                }
            }
        }
        
        private readonly Dictionary<GameObject, List<LeaseHandle>> createdPools = new Dictionary<GameObject, List<LeaseHandle>>();
        private readonly Dictionary<int, Transform> lanes = new Dictionary<int, Transform>();
        private readonly Dictionary<GameObject, LeaseHandle> objectToHandleDictionary = new Dictionary<GameObject, LeaseHandle>();
        private Transform poolRoot;
        private bool isQuitting = false;
        
        protected override void Init()
        {
            base.Init();
            poolRoot = new GameObject("Pool").transform;
            poolRoot.parent = transform;
        }
        
        public bool IsLeasedObj(GameObject obj)
        {
            return obj != null && objectToHandleDictionary.ContainsKey(obj);
        }
        
        public bool IsLeasedObj(MonoBehaviour obj)
        {
            return obj != null && objectToHandleDictionary.ContainsKey(obj.gameObject);
        }

        public T LeaseObject<T>(T template, Transform parent) where T : MonoBehaviour
        {
            T newObject = LeaseObject(template);
            newObject.transform.SetParent(parent);
            newObject.transform.position = parent.position;
            
            return newObject;
        }
        
        public GameObject LeaseObject(GameObject template, Transform parent)
        {
            GameObject obj = LeaseObject(template);
            obj.transform.SetParent(parent);
            obj.transform.position = parent.position;
			
            return obj;
        }

        public T LeaseObject<T>(T template) where T : MonoBehaviour
        {
            GameObject newObject = LeaseObject(template.gameObject);
            if (newObject.TryGetComponent(out T component))
            {
                return component;
            }
            Destroy(newObject);
            
            return null;
        }
        
        public GameObject LeaseObject(GameObject template)
        {
            if (template == null)
            {
                return null;
            }
			
            if(!createdPools.TryGetValue(template, out List<LeaseHandle> pool))
            {
                pool = new List<LeaseHandle>();
                createdPools[template] = pool;
            }

            GameObject leaseObject;
            for (int i = pool.Count - 1; i >= 0; i--)
            {
                LeaseHandle lease = pool[i];

                if (lease == null)
                {
                    pool.RemoveAt(i);
                }
                else if (lease.TryLease(out leaseObject))
                {
                    leaseObject.transform.rotation = template.transform.rotation;
                    leaseObject.transform.localScale = template.transform.localScale;
                    leaseObject.SetActive(true);
					
                    return leaseObject;
                }
            }

            LeaseHandle newLease = new LeaseHandle(template);
            objectToHandleDictionary.Add(newLease.Obj, newLease);
            pool.Add(newLease);
            newLease.TryLease(out leaseObject);
			
            return leaseObject;
        }
        
        private IEnumerator DelayedDisable(LeaseHandle handle)
        {
            yield return 0;
            if (handle != null && handle.Obj != null)
            {
                handle.Obj.SetActive(false);
            }
        }

        public void ReturnLeasedObj(MonoBehaviour behaviour)
        {
            ReturnLeasedObj(behaviour.gameObject);
        }
        
        public void ReturnLeasedObj(GameObject obj)
        {
            if (isQuitting || obj == null)
            {
                return;
            }
			
            if(objectToHandleDictionary.TryGetValue(obj, out LeaseHandle handle))
            {
                handle.Return();
            }
            else
            {
                obj.SetActive(false);
            }
			
            obj.transform.localPosition = Vector3.zero;
        }

        public void ReturnAll(GameObject template)
        {
            if (createdPools.TryGetValue(template, out List<LeaseHandle> handles))
            {
                foreach (LeaseHandle handle in handles)
                {
                    handle.Return();
                }
            }
        }
        
        private void RemoveObj(GameObject template, LeaseHandle handle, bool destroyOnRemove = true)
        {
            if (isQuitting)
            {
                return;
            }
            if (createdPools.TryGetValue(template, out List<LeaseHandle> list))
            {
                list.Remove(handle);
                if (destroyOnRemove)
                {
                    Destroy(handle.Obj);
                }
            }
        }

        public void ReturnObjectsOfType(params System.Type[] targetTypes)
        {
            foreach (System.Type targetType in targetTypes)
            {
                ReturnObjectsOfType(targetType);
            }
        }
		
        public void ReturnObjectsOfType(System.Type targetType)
        {
            foreach (KeyValuePair<GameObject, List<LeaseHandle>> kvp in createdPools)
            {
                bool typeCheckPassed = false;
				
                for (int i = kvp.Value.Count - 1; i >= 0; i--)
                {
                    if (kvp.Value[i] != null && kvp.Value[i].InUse)
                    {
                        if (!typeCheckPassed)
                        {
                            if (kvp.Value[i] != null && kvp.Value[i].HasComponent(targetType))
                            {
                                typeCheckPassed = true;
                            }
                            else
                            {
                                break;
                            }
                        }

                        if (kvp.Value[i] != null)
                        {
                            kvp.Value[i].Return();
                        }
                    }
                }
            }
        }
        
        public void CleanPool(GameObject template)
        {
            if (isQuitting)
            {
                return;
            }
            if (createdPools.TryGetValue(template, out List<LeaseHandle> poolHandles))
            {
                for(int i = poolHandles.Count - 1; i >= 0; i--)
                {
                    LeaseHandle handle = poolHandles[i];
                    if (handle == null)
                    {
                        poolHandles.RemoveAt(i);
                    }
                    else if (!handle.InUse)
                    {
                        poolHandles.Remove(handle);
                        Destroy(handle.Obj);
                    }
                }
            }
        }

        public void CleanAllPools()
        {
            if (isQuitting)
            {
                return;
            }

            foreach (KeyValuePair<GameObject, List<LeaseHandle>> kvp in createdPools)
            {
                CleanPool(kvp.Key);
            }
        }
        
        public void ClearPool(GameObject template, bool destroyEvenIfActive = false)
        {
            if (isQuitting)
            {
                return;
            }
			
            if (createdPools.TryGetValue(template, out List<LeaseHandle> pool))
            {
                foreach (LeaseHandle handle in pool)
                {
                    if (destroyEvenIfActive || !handle.InUse)
                    {
                        pool.Remove(handle);
                        Destroy(handle.Obj);
                    }
                }

                pool.Clear();
            }
        }
        
        public void ClearAllPools()
        {
            if (isQuitting)
            {
                return;
            }
            foreach (KeyValuePair<GameObject, List<LeaseHandle>> kvp in createdPools)
            {
                foreach (LeaseHandle handle in kvp.Value)
                {
                    Destroy(handle.Obj);						
                }
                kvp.Value.Clear();

                if (kvp.Key != null && lanes.TryGetValue(kvp.Key.GetHashCode(), out Transform trans))
                {
                    Destroy(trans.gameObject);
                }
            }
			
            createdPools.Clear();
            lanes.Clear();
        }

        public int GetPoolCount(GameObject template)
        {
            if (createdPools.TryGetValue(template, out List<LeaseHandle> handles))
            {
                return handles.Count;
            }

            return 0;
        }

        private void OnApplicationQuit()
        {
            isQuitting = true;
        }
        
        private Transform GetLane(GameObject template)
        {
            if (lanes.TryGetValue(template.GetHashCode(), out Transform holder))
            {
                return holder;
            }

            holder = new GameObject("Lane(" + template.name + ")").transform;
            lanes[template.GetHashCode()] = holder;
            holder.SetParent(poolRoot);
            return holder;
        }
    }
}