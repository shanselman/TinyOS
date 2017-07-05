// ------------------------------------------------------------------------------
// <copyright from='2002' to='2002' company='Scott Hanselman'>
//    Copyright (c) Scott Hanselman. All Rights Reserved.   
// </copyright> 
// ------------------------------------------------------------------------------
//
// Scott Hanselman's Tiny Academic Virtual CPU and OS
// Copyright (c) 2002, Scott Hanselman (scott@hanselman.com)
// All rights reserved.
// 
// A BSD License
// Redistribution and use in source and binary forms, with or without modification,
// are permitted provided that the following conditions are met:
// 
// Redistributions of source code must retain the above copyright notice, 
// this list of conditions and the following disclaimer. 
// Redistributions in binary form must reproduce the above copyright notice,
// this list of conditions and the following disclaimer in the documentation 
// and/or other materials provided with the distribution. 
// Neither the name of Scott Hanselman nor the names of its contributors
// may be used to endorse or promote products derived from this software without
// specific prior written permission. 
// THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" 
// AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO,
// THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR 
// PURPOSE ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT OWNER OR CONTRIBUTORS 
// BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL
// DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; 
// LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY
// THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT (INCLUDING NEGLIGENCE 
// OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF
// THE POSSIBILITY OF SUCH DAMAGE.
//
namespace Hanselman.CST352 
{
    using System;
    using System.Collections;
    
    
    /// <summary>
    ///       A collection that stores <see cref='Hanselman.CST352.Instruction'/> objects.
    /// </summary>
    /// <seealso cref='Hanselman.CST352.InstructionCollection'/>
    [Serializable()]
    public class InstructionCollection : CollectionBase {
        
        /// <summary>
        ///       Initializes a new instance of <see cref='Hanselman.CST352.InstructionCollection'/>.
        /// </summary>
        public InstructionCollection() {
        }
        
        /// <summary>
        ///       Initializes a new instance of <see cref='Hanselman.CST352.InstructionCollection'/> based on another <see cref='Hanselman.CST352.InstructionCollection'/>.
        /// </summary>
        /// <param name='value'>
        ///       A <see cref='Hanselman.CST352.InstructionCollection'/> from which the contents are copied
        /// </param>
        public InstructionCollection(InstructionCollection value) {
            this.AddRange(value);
        }
        
        /// <summary>
        ///       Initializes a new instance of <see cref='Hanselman.CST352.InstructionCollection'/> containing any array of <see cref='Hanselman.CST352.Instruction'/> objects.
        /// </summary>
        /// <param name='value'>
        ///       A array of <see cref='Hanselman.CST352.Instruction'/> objects with which to intialize the collection
        /// </param>
        public InstructionCollection(Instruction[] value) {
            this.AddRange(value);
        }
        
        /// <summary>
        /// Represents the entry at the specified index of the <see cref='Hanselman.CST352.Instruction'/>.
        /// </summary>
        /// <param name='index'>The zero-based index of the entry to locate in the collection.</param>
        /// <value>
        ///    The entry at the specified index of the collection.
        /// </value>
        /// <exception cref='System.ArgumentOutOfRangeException'><paramref name='index'/> is outside the valid range of indexes for the collection.</exception>
        public Instruction this[int index] {
            get {
                return ((Instruction)(List[index]));
            }
            set {
                List[index] = value;
            }
        }
        
        /// <summary>
        ///    Adds a <see cref='Hanselman.CST352.Instruction'/> with the specified value to the 
        ///    <see cref='Hanselman.CST352.InstructionCollection'/> .
        /// </summary>
        /// <param name='value'>The <see cref='Hanselman.CST352.Instruction'/> to add.</param>
        /// <returns>
        ///    The index at which the new element was inserted.
        /// </returns>
        /// <seealso cref='Hanselman.CST352.InstructionCollection.AddRange(Instruction[])'/>
        public int Add(Instruction value) {
            return List.Add(value);
        }
        
        /// <summary>
        /// Copies the elements of an array to the end of the <see cref='Hanselman.CST352.InstructionCollection'/>.
        /// </summary>
        /// <param name='value'>
        ///    An array of type <see cref='Hanselman.CST352.Instruction'/> containing the objects to add to the collection.
        /// </param>
        /// <returns>
        ///   None.
        /// </returns>
        /// <seealso cref='Hanselman.CST352.InstructionCollection.Add'/>
        public void AddRange(Instruction[] value) {
            for (int i = 0; (i < value.Length); i = (i + 1)) {
                this.Add(value[i]);
            }
        }
        
        /// <summary>
        ///     
        ///       Adds the contents of another <see cref='Hanselman.CST352.InstructionCollection'/> to the end of the collection.
        ///    
        /// </summary>
        /// <param name='value'>
        ///    A <see cref='Hanselman.CST352.InstructionCollection'/> containing the objects to add to the collection.
        /// </param>
        /// <returns>
        ///   None.
        /// </returns>
        /// <seealso cref='Hanselman.CST352.InstructionCollection.Add'/>
        public void AddRange(InstructionCollection value) {
            for (int i = 0; (i < value.Count); i = (i + 1)) {
                this.Add(value[i]);
            }
        }
        
        /// <summary>
        /// Gets a value indicating whether the 
        ///    <see cref='Hanselman.CST352.InstructionCollection'/> contains the specified <see cref='Hanselman.CST352.Instruction'/>.
        /// </summary>
        /// <param name='value'>The <see cref='Hanselman.CST352.Instruction'/> to locate.</param>
        /// <returns>
        /// <see langword='true'/> if the <see cref='Hanselman.CST352.Instruction'/> is contained in the collection; 
        ///   otherwise, <see langword='false'/>.
        /// </returns>
        /// <seealso cref='Hanselman.CST352.InstructionCollection.IndexOf'/>
        public bool Contains(Instruction value) {
            return List.Contains(value);
        }
        
        /// <summary>
        /// Copies the <see cref='Hanselman.CST352.InstructionCollection'/> values to a one-dimensional <see cref='System.Array'/> instance at the 
        ///    specified index.
        /// </summary>
        /// <param name='array'>The one-dimensional <see cref='System.Array'/> that is the destination of the values copied from <see cref='Hanselman.CST352.InstructionCollection'/> .</param>
        /// <param name='index'>The index in <paramref name='array'/> where copying begins.</param>
        /// <returns>
        ///   None.
        /// </returns>
        /// <exception cref='System.ArgumentException'><paramref name='array'/> is multidimensional. -or- The number of elements in the <see cref='Hanselman.CST352.InstructionCollection'/> is greater than the available space between <paramref name='index'/> and the end of <paramref name='array'/>.</exception>
        /// <exception cref='System.ArgumentNullException'><paramref name='array'/> is <see langword='null'/>. </exception>
        /// <exception cref='System.ArgumentOutOfRangeException'><paramref name='index'/> is less than <paramref name='array'/>'s lowbound. </exception>
        /// <seealso cref='System.Array'/>
        public void CopyTo(Instruction[] array, int index) {
            List.CopyTo(array, index);
        }
        
        /// <summary>
        ///    Returns the index of a <see cref='Hanselman.CST352.Instruction'/> in 
        ///       the <see cref='Hanselman.CST352.InstructionCollection'/> .
        /// </summary>
        /// <param name='value'>The <see cref='Hanselman.CST352.Instruction'/> to locate.</param>
        /// <returns>
        /// The index of the <see cref='Hanselman.CST352.Instruction'/> of <paramref name='value'/> in the 
        /// <see cref='Hanselman.CST352.InstructionCollection'/>, if found; otherwise, -1.
        /// </returns>
        /// <seealso cref='Hanselman.CST352.InstructionCollection.Contains'/>
        public int IndexOf(Instruction value) {
            return List.IndexOf(value);
        }
        
        /// <summary>
        /// Inserts a <see cref='Hanselman.CST352.Instruction'/> into the <see cref='Hanselman.CST352.InstructionCollection'/> at the specified index.
        /// </summary>
        /// <param name='index'>The zero-based index where <paramref name='value'/> should be inserted.</param>
        /// <param name=' value'>The <see cref='Hanselman.CST352.Instruction'/> to insert.</param>
        /// <returns>None.</returns>
        /// <seealso cref='Hanselman.CST352.InstructionCollection.Add'/>
        public void Insert(int index, Instruction value) {
            List.Insert(index, value);
        }
        
        /// <summary>
        ///    Returns an enumerator that can iterate through 
        ///       the <see cref='Hanselman.CST352.InstructionCollection'/> .
        /// </summary>
        /// <returns>None.</returns>
        /// <seealso cref='System.Collections.IEnumerator'/>
        public new InstructionEnumerator GetEnumerator() {
            return new InstructionEnumerator(this);
        }
        
        /// <summary>
        ///     Removes a specific <see cref='Hanselman.CST352.Instruction'/> from the 
        ///    <see cref='Hanselman.CST352.InstructionCollection'/> .
        /// </summary>
        /// <param name='value'>The <see cref='Hanselman.CST352.Instruction'/> to remove from the <see cref='Hanselman.CST352.InstructionCollection'/> .</param>
        /// <returns>None.</returns>
        /// <exception cref='System.ArgumentException'><paramref name='value'/> is not found in the Collection. </exception>
        public void Remove(Instruction value) {
            List.Remove(value);
        }
        
		/// <summary>
		/// Provided for "foreach" support with this collection
		/// </summary>
        public class InstructionEnumerator : object, IEnumerator {
            
            private IEnumerator baseEnumerator;
            
            private IEnumerable temp;
            
			/// <summary>
			/// Public constructor for an InstructionEnumerator
			/// </summary>
			/// <param name="mappings">The <see cref="InstructionCollection"/>we are going to iterate over</param>
            public InstructionEnumerator(InstructionCollection mappings) {
                this.temp = ((IEnumerable)(mappings));
                this.baseEnumerator = temp.GetEnumerator();
            }
            
			/// <summary>
			/// The current <see cref="Instruction"/>
			/// </summary>
            public Instruction Current {
                get {
                    return ((Instruction)(baseEnumerator.Current));
                }
            }
            
			/// <summary>
			/// The current IEnumerator interface
			/// </summary>
            object IEnumerator.Current {
                get {
                    return baseEnumerator.Current;
                }
            }
            
			/// <summary>
			/// Move to the next Instruction
			/// </summary>
			/// <returns>true or false based on success</returns>
            public bool MoveNext() {
                return baseEnumerator.MoveNext();
            }
            
			/// <summary>
			/// Move to the next Instruction
			/// </summary>
			/// <returns>true or false based on success</returns>
			bool IEnumerator.MoveNext() 
			{
                return baseEnumerator.MoveNext();
            }
            
			/// <summary>
			/// Reset the cursor
			/// </summary>
			public void Reset() 
			{
                baseEnumerator.Reset();
            }
            
			/// <summary>
			/// Reset the cursor
			/// </summary>
			void IEnumerator.Reset() 
			{
                baseEnumerator.Reset();
            }
        }
    }
}
