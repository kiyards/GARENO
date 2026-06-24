using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public static class DictUtils
{
    public static string DictionaryToString(Dictionary<int, int> dictionary)
    {
        string dictionaryString = "";
        foreach (KeyValuePair<int, int> keyValues in dictionary)
        {
            dictionaryString += keyValues.Key + "#" + keyValues.Value + "@";
        }
        return dictionaryString.TrimEnd('@', ' ');
    }

    public static int DictionaryToInt(Dictionary<int, int> dictionary)
    {
        int sum = 0; //sum of all values in dictionary

        foreach (KeyValuePair<int, int> keyValues in dictionary)
        {
            //since the value is number, sum those up
            sum += keyValues.Value;
        }

        return sum;
    }

    /// <summary>
    /// New dictionary from string
    /// </summary>
    public static Dictionary<int, int> StringToDictionary(string stringToParse)
    {
        //Debug.Log("Parsing " + stringToParse);

        Dictionary<int, int> dict = new Dictionary<int, int>();

        if (string.IsNullOrEmpty(stringToParse))
        {
            //Debug.Log("String is empty, returning empty dictionary");
            return dict;
        }

        //0#1@1#10@3#13
        //id#value@id#value

        //split string by @ if any
        if (stringToParse.Contains('@'))
        {
            string[] encountered = stringToParse.Split('@');

            //0#1, 1#10

            //loop through each element in the array
            for (int i = 0; i < encountered.Length; i++)
            {
                //split by #, 1st value is id (key), 2nd value is value
                string[] values = encountered[i].Split('#');
                dict.Add(int.Parse(values[0]), int.Parse(values[1]));
            }

        }
        else
        {
            //does not contain @, only has 1 entry
            string[] dictString = stringToParse.Split('#');
            dict.Add(int.Parse(dictString[0]), int.Parse(dictString[1]));
        }

        return dict;
    }

    public static void AddToDictionary(this Dictionary<int, int> targetDict, Dictionary<int, int> dictToAdd)
    {
        foreach (KeyValuePair<int, int> keyValues in dictToAdd)
        {
            //if contains the key
            if (targetDict.ContainsKey(keyValues.Key))
            {
                //increment the value
                targetDict[keyValues.Key] += dictToAdd[keyValues.Key];
            }
            else
            {
                //make new key
                targetDict.Add(keyValues.Key, keyValues.Value);
            }
        }
    }

    public static int SumOfDictionaryValues<T>(Dictionary<T, int> dictToCheck)
    {
        int sum = 0;

        foreach (KeyValuePair<T, int> keyValues in dictToCheck)
        {
            //add up values
            sum += keyValues.Value;
        }

        return sum;
    }

    public static HashSet<int> StringToHashSet(string stringToParse)
    {
        HashSet<int> hash = new();
        if (string.IsNullOrEmpty(stringToParse))
            return hash;
        string[] ids = stringToParse.Split('@');
        foreach (var id in ids)
            hash.Add(int.Parse(id));
        return hash;
    }
    public static string HashSetToString(HashSet<int> hashToParse)
    {
        string str = "";
        foreach (var item in hashToParse)
            str += $"{item}@";
        return str.TrimEnd('@', ' ');
    }
    public static void AddToHashSet<T>(this HashSet<T> targetHash, HashSet<T> hashToAdd)
    {
        foreach (var item in hashToAdd)
        {
            if (!targetHash.Contains(item))
                targetHash.Add(item);
        }
    }
}