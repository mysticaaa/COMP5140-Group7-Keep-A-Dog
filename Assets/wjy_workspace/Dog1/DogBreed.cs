using System;
using UnityEngine;

namespace Wjy.DogTraining
{
    internal sealed class DogBreed
    {
        internal const string DefaultId = "border-collie";
        internal const float ShepherdHeight = .95f;
        internal static readonly DogBreed Shepherd = new DogBreed("german-shepherd", "德国牧羊犬", ShepherdHeight);
        internal static readonly DogBreed[] Options =
        {
            new DogBreed(DefaultId, "边境牧羊犬", .69f),
            new DogBreed("shiba-inu", "柴犬", .58f),
            Shepherd,
            new DogBreed("boxer", "拳师犬", .77f),
            new DogBreed("dalmatian", "斑点狗", .80f),
            new DogBreed("golden-retriever", "金毛寻回犬", .78f),
            new DogBreed("great-dane", "大丹犬", 1.02f),
            new DogBreed("husky", "哈士奇", .83f),
            new DogBreed("labrador-retriever", "拉布拉多", .76f),
            new DogBreed("pitbull", "比特犬", .69f),
            new DogBreed("rottweiler", "罗威纳犬", .82f)
        };

        internal readonly string Id, DisplayName;
        internal readonly float Height;
        internal bool IsOriginal => Id == Shepherd.Id;

        private DogBreed(string id, string displayName, float height)
        {
            Id = id;
            DisplayName = displayName;
            Height = height;
        }

        internal static DogBreed Find(string id) =>
            Array.Find(Options, breed => string.Equals(breed.Id, id, StringComparison.Ordinal));

        internal GameObject LoadModel() =>
            Resources.Load<GameObject>(IsOriginal ? "Dog1/GermanShepherd" : "Dog1/Breeds/" + Id);
    }
}
