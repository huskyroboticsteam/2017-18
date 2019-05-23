#include "Controller.hpp"
#include "RobotEKF.hpp"
#include "Server.hpp"
#include "autonomous/utils.hpp"
#include "kalman/ekfilter.hpp"
#include <cstring>
#include <iostream>
#include <math.h>
#include <thread>
#include "../../../ZedDepth/zed-depth.h"

#define MOVE_SPEED 0x7FFF / 2

#define DATA_GPS 10
#define DATA_MAG 11

#define SET_SPEED 0x01
#define HEADING_CHANGE 0x02

#define TARGET_LAT 123.0
#define TARGET_LNG 321.0

#define ANGLE_TOL 20.

constexpr float TARGET_TOL = 4.f; // tolerance distance for testing if we've reached target
constexpr float TARGET_TOL_SQ = TARGET_TOL * TARGET_TOL;
constexpr float AUTO_TURN_RANGE = 90;
constexpr float GPS_MULT = 1e6;
/*
LATITUDE IS X
LONGITUDE IS
*/

cv::Mat image;

RP::point origin;
RP::point max_point;

int main() {
  std::deque<RP::point> targetSites(0);
  origin.x = 47.648531;
  origin.y = -122.309705;
  max_point.x = 47.654537;
  max_point.y = -122.302730;
  char answer;
  std::cout << "Origin: " << origin.x << " " << origin.y << std::endl;
  std::cout << "Change origin? (y/n) ";
  std::cin >> answer;
  if(answer == 'y') {
    std::cout << "lat: ";
    std::cin >> origin.x;
    std::cout << "lng: ";
    std::cin >> origin.y;
                   }
    std::cout << "Max point: " << max_point.x << " " << max_point.y << std::endl;
    std::cout << "Change max point? (y/n) ";
    std::cin >> answer;
    if(answer == 'y') {
    std::cout << "lat: ";
    std::cin >> max_point.x;
    std::cout << "lng: ";
    std::cin >> max_point.y;
  }
  std::cout << "Enter target coordinates: " << std::endl;
  float lat = 0;
  float lng = 0;
  RP::point p;
  std::cin >> p.x;
  std::cin >> p.y;
  while (p.x != -1) {
    targetSites.push_back(p);
    std::cin >> p.x;
    std::cin >> p.y;
  }
  for (int i = 0; i < targetSites.size(); i++) {
    std::cout << targetSites[i].x << ", " << targetSites[i].y << std::endl;
  }
  std::cout << "Enter Current Position: " << std::flush;
  std::cin >> p.x;
  std::cin >> p.y;
  RP::Controller controller(p, targetSites);
  zdInit();
  std::cout << "Finished constructing" << std::endl;
  controller.update();
}

namespace RP {

Controller::Controller(const point &cur_pos, std::deque<point> targetSites)
    : server(),
      detector("Tennisball/data/final_models/frozen_inference_graph.pb",
               "Tennisball/data/final_models/graph.pbtxt"),
      receiverThread(&RP::Server::data_receiver_loop, &server),
      pather(origin*GPS_MULT, max_point*GPS_MULT, RP::point{cur_pos.x, cur_pos.y}*GPS_MULT, targetSites[0]*GPS_MULT)
{
    this->targetSites = targetSites;
    state = FOLLOW_PATH;
    curr_lat = cur_pos.x*GPS_MULT;
    curr_lng = cur_pos.y*GPS_MULT;
    std::cout << "Path size: " << pather.get_cur_path().size() << std::endl;
    std::cout << "Tried to initialize kalman filter" << std::endl;

    Kalman::KMatrix<float, 1, true> P0(STATE_VEC_SIZE, STATE_VEC_SIZE);
    Kalman::KVector<float, 1, true> x(STATE_VEC_SIZE);

    filter.setP0(P0);

    x(1) = curr_lat;
    x(2) = curr_lng;
    x(3) = 0;
    x(4) = 0;

    filter.init(x, P0);

    std::cout << "Initialized kalman filter" << std::endl;
}

// using given packet data and server send a packet containing either a
// direction or motor power
bool Controller::setDirection(float delta_heading) {
    std::vector<unsigned char> data(4);
    std::memcpy(&data[0], &delta_heading, 4);
    return server.send_action(delta_heading, HEADING_CHANGE);
}

bool Controller::setSpeed(float speed) {
    std::vector<unsigned char> data(4);
    std::memcpy(&data[0], &speed, 4);
    return server.send_action(data, SET_SPEED);
}

void Controller::update() {

    pather.compute_path();
    std::cout << "Update loop started" << std::endl;
    // step 1: get obstacle data from camera
    while (!targetSites.empty()) {
    //    std::cout << "target sites is non-empty " << targetSites.size()
                  //<< std::endl;
        // TODO: actually get obstacle data from camera
        // std:vector<obstacleVector> obstacles = METHOD_GOES_HERE
        // Bogus obstacle data for testing

//        std::vector<obstacleVector> obstacles{
//            obstacleVector{1, 2}, obstacleVector{3, 4}, obstacleVector{5, 6}};

        std::vector<std::pair<cv::Rect, float>> raw_obstacles;// = get_obstacle_data(image);
        // step 2: wait for server to give current location
        // Note: If Scarlet changes size of Timestamp, be sure to update the
        // parsePacket paramaters below

        // TODO: make this a vector or shared_ptr
        //std::cout << "Listening" << std::endl;

        char packet_buf[buf_size];
        std::cout << "Current state: ";
        switch(state) {
          case FOLLOW_PATH:
            
            std::cout << "FOLLOW PATH" << std::endl;
            break;
          case SPIRAL:

            std::cout << "SPIRAL" << std::endl;
            break;
          case FOUND_BALL:
            std::cout << "FOUND BALL" << std::endl;
            break;
        }
        std::cout << "Current turn state: " << turnstate << std::endl;
        Kalman::KVector<float, 1, true> u(2);
        if(server.get_packet_data(packet_buf)) { 
            float received_lat;
            float received_lng;
            float received_dir;
            memcpy(&received_lat, packet_buf, 4);
            memcpy(&received_lng, packet_buf+4, 4);
            memcpy(&received_dir, packet_buf+8, 4);
            
            
              curr_dir = received_dir;
            if(received_lat < 10 && received_lng < 10) {
              std::cout << "Received bad GPS data, ignoring" << std::endl;
            } else {
              received_lat *= GPS_MULT;
              received_lng *= GPS_MULT;
              
              
              std::cout << "Started stepping kalman filter with received data" << std::endl;
              std::cout << "Received heading: " << curr_dir;
              Kalman::KVector<float, 1, true> z(4);
              z(1) = received_lat;
              z(2) = received_lng;
              // std::cout << "Success: Initialized the things" << std::endl;
              filter.step(u, z);
              curr_lat = filter.getX()(1);
              curr_lng = filter.getX()(2);
              std::cout << "Kalman says: lat: " << curr_lat << " lng: " << curr_lng
                      << std::endl;
            }
        } else { //step the filter without new data
            filter.timeUpdateStep(u);
        }
        pather.set_pos(RP::point{curr_lat, curr_lng});
        // std::cout << "Controller got a packet" << std::endl;
        point nextPoint{0.0, 0.0};
        //std::cout << state << std::endl;
        if (state == FOLLOW_PATH) {
            if (in_spiral_radius()) {
              std::cout << "We started spiraling! yay!" << std::endl;
                state = SPIRAL;
                // spiralPts = RP::generate_spiral(0.1, 100, curr_lng,
                // curr_lat);
            } else if (found_ball()) {
                state = FOUND_BALL;
            } else {
                if (turning) 
                {
                    switch (turnstate) {
                    case TOWARD_TARGET:
                        //if (angleCloseEnough(curr_dir, tar_angle, ANGLE_TOL)) {
                        if(angleCloseEnough(curr_dir, tar_angle,  ANGLE_TOL)) {
                            pather.compute_path();
                            auto path = pather.get_cur_path();
                            if (path.size() == 0)
                                break;
                            // TODO this is a hack. later, implement flag in
                            // mapper that tells if tar is the final target or
                            // even better, implement a better algorithm
                            if (path.size() == 1 &&
                                dist_sq(point{curr_lat, curr_lng},
                                        path.back()) <= TARGET_TOL_SQ &&
                                same_point(path.back(), pather.tar_point)) {
                                printf("Target reached.\n");
                                turnstate = FIND_BALL;
                                break;
                            }
                            tar_angle = normalize_angle_deg(get_target_angle());
                            if (angleCloseEnough(curr_dir, tar_angle, ANGLE_TOL)) {
                                // printf("reached turning target of %f\n",
                                // tar_angle);
                                // TODO recompute tar_angle again and
                                // iteratively turn until angle is the same as
                                // target angle
                                turn_and_go();
                            } else {
                                timer.reset(); // turn again
                            }
                        } else {
                             printf("turning toward target %f\n", tar_angle);
                             
                            sendPacket(0, tar_angle);
                        }

                        break;
                    case SURVEY_COUNTERCW:
                        if (angleCloseEnough(curr_dir, tar_angle, ANGLE_TOL)) {
                            tar_angle = normalize_angle_deg(orig_angle - AUTO_TURN_RANGE / 2.f);
                            turnstate = SURVEY_CW;
                            // printf("turning cw toward %f\n", tar_angle);
                            break;
                        }
                        sendPacket(0, tar_angle);
                        break;
                    case SURVEY_CW:
                        if (angleCloseEnough(curr_dir, tar_angle, ANGLE_TOL)) {
                            auto tar_point = pather.get_cur_next_point();
                            if (tar_point.x == INFINITY)
                                break;
                            // printf("target point: %f, %f\n", tar_point.x,
                            // tar_point.y);
                            tar_angle = normalize_angle_deg(atan2(tar_point.y - curr_lng,
                                              tar_point.x - curr_lat) *
                                        180 / M_PI);
                            // printf("turning toward target %f\n", tar_angle);
                            turnstate = BACK_TO_TARGET;
                            break;
                        }
                        sendPacket(0, tar_angle);
                        break;
                    case BACK_TO_TARGET:
                        if (angleCloseEnough(curr_dir, tar_angle, ANGLE_TOL)) {
                            // printf("reached turning target of %f\n",
                            // tar_angle);
                            pather.compute_path();
                            tar_angle = normalize_angle_deg(get_target_angle());
                            if (angleCloseEnough(curr_dir, tar_angle, ANGLE_TOL)) {
                                printf("close enough to %f\n", tar_angle);
                                turn_and_go();
                            }
                            break;
                        }
                        sendPacket(0, tar_angle);
                        break;
                    case FIND_BALL:
                        state = SPIRAL;
                        break;
                    case FINISHED:
                        // TODO send back whatever instructions needed
                        break;
                    }
                } else {
                    std::vector<point> path = pather.get_cur_path();
                    if (path.empty())
                        return;
                    if (timer.elapsed() > last_move_time) {
                        tar_angle = normalize_angle_deg(get_target_angle());
                        if (path.size() == 1) {
                            // if no obstacle, go straight to point
                            turning = true;
                            turnstate = TOWARD_TARGET;
                        } else {
                            timer.reset();
                            pather.compute_path();
                            tar_angle = normalize_angle_deg(get_target_angle());
                            turnstate = TOWARD_TARGET;
                            turning = true;
                        }
                    } else {
                        sendPacket(MOVE_SPEED, curr_dir);
                    }
                }
            }
        } else if (state == SPIRAL) {
            if (found_ball()) {
                state = FOUND_BALL;
            } else {
                // get and remove first element of spiralPts
                dst = spiralPts.front();
                spiralPts.pop_front();
                nextPoint = pather.get_cur_next_point();
            }
        } else { // if state is FOUND_BALL
            if (targetSites.size() > 0) {
                // get and remove first element of targets
                dst = targetSites.front();
                targetSites.pop_front();
                pather.set_tar(dst);
            }
            
        }

        // step 3: use current location and obstacle data to update map
        //TODO: Use FOV of Camera to determine where the obstacles actually are
        std::vector<RP::line> obstacles;
        for (auto raw_obstacle : raw_obstacles) {
            std::cout << "Obstacle: distance: " << raw_obstacle.second << std::endl;
            double obstacle_width = raw_obstacle.first.width;
            float obstacle_dist = raw_obstacle.second;
            /*
             * heading := current heading vector normalized
             * ortho = orthogonal heading normalized
             * pt1 cur_pos + (heading * obstacle_distance + ortho * half_width)
             * pt2 cur_pos + (heading * obstacle_distance - ortho * half_width)
             * */
            std::vector<RP::line> obstacles;
            point norm_heading = point{cos(curr_dir), sin(curr_dir)};
            point ortho_norm = get_ortho(line{norm_heading, norm_heading*2}, false);
            point a = point{curr_lat, curr_lng} + norm_heading*obstacle_dist
                      + ortho_norm*(obstacle_width/2);
            point b = point{curr_lat, curr_lng} + norm_heading*obstacle_dist
                      - ortho_norm*(obstacle_width/2);
            RP::line obstacleLine{a, b}; 
            
            obstacles.push_back(obstacleLine);
            cv::rectangle(image, raw_obstacle.first, cv::Scalar(0, 255, 0));
        }
        pather.add_obstacles(obstacles);

        // step 5: use next point to send packets specifying new direction and
        // speed to proceed
        short heading =
            (short)(180 / M_PI *
                    atan2(nextPoint.y - curr_lng, nextPoint.x - curr_lat));
        // setDirection(delta_heading);
        //  setSpeed(1.0); // TODO: figure out how setting speed and heading
        // actually works
        //sendPacket(0x7FFF / 2, heading);
        tar_angle = normalize_angle_deg(get_target_angle());
        std::cout << "tar_angle: " << tar_angle << std::endl;
                
        //for(char key = ' '; key != 'q'; key = cv::waitKey(10)) {
            //cv::imshow("Camera", image);
            //cv::waitKey(10);

        
        }
    
}

void RP::Controller::turn_and_go() {
    // pather.compute_path();
    auto path = pather.get_cur_path();
    float dist = sqrt(dist_sq(path.front(), point{curr_lat, curr_lng}));
    if (path.size() == 1) {
        last_move_time = 0.5f;
        last_move_speed = 1.f;
    } else {
        // TODO tune speed
        last_move_time = 0.4f + 0.01f * dist;
        last_move_speed = std::min(0.4f + 0.01f * dist, 1.f);
        printf("time: %f, speed: %f, dist: %f\n", last_move_time,
               last_move_speed, dist);
    }
    turning = false;
    timer.reset();
}

float RP::Controller::get_target_angle() {
    auto path = pather.get_cur_path();
    std::vector<point>::iterator next = path.begin();

    // prevent robot from getting stuck at one point
    for (next = path.begin();
         next != path.end() &&
         same_point(*next, point{curr_lat, curr_lng}, 1.f);
         next++);
    return atan2(next->y - curr_lng, next->x - curr_lat) * 180 / M_PI;
}

bool Controller::in_spiral_radius() { return dist_sq(point{curr_lat, curr_lng}
    , targetSites.front()) < TARGET_TOL;  }

bool Controller::found_ball() {
  //cv::imshow("MAH BALL", image);
  //cv::waitKey(0);
  return false;
  return detector.performDetection(image).size() == 1;
}

bool Controller::sendPacket(signed short speed, float fheading) {
    unsigned short heading = static_cast<unsigned short>(RP::normalize_angle_deg(fheading)); 
    std::vector<unsigned char> data(5);
    data[0] = 0;
    memcpy(&data.data()[1], &speed, 2);
    memcpy(&data.data()[3], &heading, 2);
    std::cout << "Heading: " << heading << " speed: " << speed << std::endl;
    return server.send_action(data);
}

bool Controller::sendDestinationPacket() {
    std::vector<unsigned char> data(1);
    data[0] = 1;
    return server.send_action(data);
}


void Controller::addObstacle(float dist1, float dir1, float dist2, float dir2) {
    // Need to update this for use
    // RP::point latlng1 = convertToLatLng(dist1, dir1);
    // RP::point latlng2 = convertToLatLng(dist2, dir2);
    // map.add_obstacle(latlng1, latlng2);
}

void Controller::foundTennisBall(float dist, float dir) {}

// angle must be in radians, dist in meters
RP::point Controller::convertToLatLng(float dist, float angle) {
    return RP::convertToLatLng(curr_lat, curr_lng, curr_dir, dist, angle);
}

} // namespace RP
