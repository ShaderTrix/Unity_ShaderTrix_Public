#ifndef BVH_INCLUDED
#define BVH_INCLUDED

#define BVH_STACK_SIZE 32
#define BVH_FLT_MAX 3.402823466e+38f

struct BvhData
{
    float3 min; //12b
    float pad0;

    float3 max; //12b
    float pad1;

    int leftIdx; //4b
    int rightIdx; //4b

    int triangleIdx; // -1 if data is not leaf //4b
    int triangleCount; //4b
};
StructuredBuffer<BvhData> _BvhDataBuffer;
struct Triangle
{
    float3 pos0; //12b
    float pad0;

    float3 pos1; //12b
    float pad1;

    float3 pos2; //12b
    float pad2;

    float3 normal; //12b
    float pad3;
};
StructuredBuffer<Triangle> _TriangleBuffer;

inline float determinant(float3 v0, float3 v1, float3 v2)
{
    return determinant(float3x3(
        v0.x, v1.x, v2.x,
        v0.y, v1.y, v2.y,
        v0.z, v1.z, v2.z
    ));
}

// Line triangle
// https://shikousakugo.wordpress.com/2012/06/27/ray-intersection-2/
inline bool LineTriangleIntersection(Triangle tri, float3 origin, float3 rayStep, out float rayScale)
{
    rayScale = BVH_FLT_MAX;

    float3 normal = tri.normal;
    float dirDot = dot(normal, rayStep);
    //if the line direction is away from the normal,return false negative number may mean its behind the triangle 
    if ( dirDot > 0 ) return false;

    float3 origin_from_pos0 = origin - tri.pos0;
    //if lineorigin is headed away from the normal of triangle ,return 
    if(dot(origin_from_pos0, normal) < 0 ) return false;

    float3 rayStep_end_from_pos0 = origin_from_pos0 + rayStep;
    //if distance between agent and triangle + velocity lineDirection is heading away from the normal,return
    if(dot(rayStep_end_from_pos0, normal) > 0 ) return false;

    //calculating the edges of triangle 
    float3 edge0 = tri.pos1 - tri.pos0;
    float3 edge1 = tri.pos2 - tri.pos0;

    const float float_epsilon = 0.001;

    float d = determinant(edge0, edge1, -rayStep);
    if ( d> float_epsilon)
    {
        float dInv = 1.0 / d;
        //used to calculate how far inside the line is in the triangle between o - 1 as per barycentirc weights 
        float u = determinant(origin_from_pos0, edge1, -rayStep) * dInv;//barycentric coordinates u 
        float v = determinant(edge0, origin_from_pos0, -rayStep) * dInv;//barycentric coordinates v 

        if ( 0<=u && u<=1 && 0<=v && (u+v)<=1)
        {
            float t = determinant(edge0, edge1, origin_from_pos0) * dInv;// finally retrieving where the intersection happened inside a triangle 
            if ( t > 0 )
            {
                rayScale = t;
                return true;
            }
        }
    }

    return false;
}
//Goes throiugh the triangle buffer and checks if any triangle has come closest to the intersection,outputs which triangle is closest
bool LineTriangleIntersectionAll(float3 origin, float3 rayStep, out float rayScale, out float3 normal)
{
    uint num, stride;
    _TriangleBuffer.GetDimensions(num, stride);

    rayScale = BVH_FLT_MAX;
    for(uint i=0; i<num; ++i)
    {
        Triangle tri = _TriangleBuffer[i];

        float tmpRayScale;
        if (LineTriangleIntersection(tri, origin, rayStep, tmpRayScale))
        {
            if ( tmpRayScale < rayScale)
            {
                rayScale = tmpRayScale;
                normal = tri.normal;
            }
        }
    }

    return rayScale != BVH_FLT_MAX;
}

// Line AABB
// http://marupeke296.com/COL_3D_No18_LineAndAABB.html
//this is a line to bounding box intersection check which is more coarse check if there line has intersected the bounding box or not 
bool LineAABBIntersection(float3 origin, float3 rayStep, BvhData data)
{
    float3 aabbMin = data.min;
    float3 aabbMax = data.max;

    float tNear = -BVH_FLT_MAX;
    float tFar  =  BVH_FLT_MAX;

    for(int axis = 0; axis<3; ++axis)
    {
        //intresting because float3 in HLSL can be indexed into...so lineDirection[0] = x , lineDirection[1] = y,lineDirection[2]=z , 
        //so each component can be accessed individually by indexing 
        float rayOnAxis = rayStep[axis];
        float originOnAxis = origin[axis];
        float minOnAxis = aabbMin[axis];
        float maxOnAxis = aabbMax[axis];
        if(rayOnAxis == 0)
        {
            if ( originOnAxis < minOnAxis || maxOnAxis < originOnAxis ) return false;
        }
        else
        {
            //this is not barrycentric but its actually just calculatng ray to box intersection,so basically this is a Slab method where we check if 
            //ray is inside all three directions of a bounding box...a bounding box is a min/max range in each direction we check if ray stays inside these ranges 
            float rayOnAxisInv = 1.0 / rayOnAxis;
            float t0 = (minOnAxis - originOnAxis) * rayOnAxisInv;
            float t1 = (maxOnAxis - originOnAxis) * rayOnAxisInv;

            float tMin = min(t0, t1);
            float tMax = max(t0, t1);

            tNear = max(tNear, tMin);
            tFar  = min(tFar, tMax);

            // float maxDist = length(lineDirection);
            // if(tFar < 0.0 || tFar < tNear || 1.0f < tNear) //collision check between 0 - 1
            // if(tFar < 0.0 || tFar < tNear || maxDist < tNear) //collision check between 0 - 1
            if (tFar < 0.0 || tFar < tNear || 1.0 < tNear) return false;
        }
    }

    return true;
}

// Line Bvh
// http://raytracey.blogspot.com/2016/01/gpu-path-tracing-tutorial-3-take-your.html
bool TraverseBvh(float3 origin, float3 rayStep, out float rayScale, out float3 normal)
{
    int stack[BVH_STACK_SIZE];

    int stackIdx = 0;
     //i++ = postIncrement ; use the old value ,then increment
    //++i = preIncrement ; increment first,then use the new value 
    stack[stackIdx++] = 0;
   //so the value for the ID becomes 1 
    rayScale = BVH_FLT_MAX;

    while(stackIdx)
    {
        stackIdx--;//removes the nurrent node from stack as its traversed 
        int BvhIdx = stack[stackIdx];
        BvhData data = _BvhDataBuffer[BvhIdx];

        //goes thtoug the BVH AABBs and check for ray AABB collision for first node     
        if ( LineAABBIntersection(origin, rayStep, data) )//if no intersection then skip the loop else go in
         {
            // Branch node
            //if the collision is not a leaf node then again go inside 
            if (data.triangleIdx < 0)
            {
                if ( stackIdx+1 >= BVH_STACK_SIZE) return false; // protect against overflow (last ID in the node)

                //since its a branch node then push its two children also into the stack as untraversed to again go through the loop  
                stack[stackIdx++] = data.leftIdx;
                stack[stackIdx++] = data.rightIdx;
            }
            //if the collion is leaf node then do ray triangle intersection test for collision
            //LeafNode
            else
            {
                for(int i=0; i<data.triangleCount; ++i)
                {
                    Triangle tri = _TriangleBuffer[i + data.triangleIdx];

                    float tmpRayScale;
                    if (LineTriangleIntersection(tri, origin, rayStep, tmpRayScale))
                    {
                        if (tmpRayScale < rayScale)
                        {
                            rayScale = tmpRayScale;
                            normal = tri.normal;
                        }
                    }
                }
            }
        }
    }

    return rayScale != BVH_FLT_MAX;// returns true if we hit atleast one triangle 
}
#endif
